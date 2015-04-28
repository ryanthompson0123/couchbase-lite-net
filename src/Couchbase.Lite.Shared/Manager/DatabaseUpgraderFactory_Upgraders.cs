﻿//
//  DatabaseUpgraderFactory_Upgraders.cs
//
//  Author:
//  	Jim Borden  <jim.borden@couchbase.com>
//
//  Copyright (c) 2015 Couchbase, Inc All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
using System;
using System.Collections.Generic;
using SQLitePCL;
using Couchbase.Lite.Util;
using System.Text;
using System.Security.Cryptography;
using System.Linq;
using Couchbase.Lite.Internal;
using Sharpen;
using System.Diagnostics;
using System.IO;

namespace Couchbase.Lite.Db
{
    internal static partial class DatabaseUpgraderFactory
    {
        private class NoopUpgrader : IDatabaseUpgrader
        {
            private readonly Database _db;

            public int NumDocs
            {
                get {
                    return _db.DocumentCount;
                }
            }

            public int NumRevs
            {
                get {
                    return -1;
                }
            }

            public NoopUpgrader(Database db, string path) 
            {
                _db = db;
            }

            #region IDatabaseUpgrader

            public Status Import()
            {
                return new Status(StatusCode.Ok);
            }

            public void Backout()
            {
                // no-op
            }

            #endregion
        }

        private class v1_upgrader : IDatabaseUpgrader
        {
            private const string TAG = "v1_upgrader";
            private readonly Database _db;
            private readonly string _path;
            private sqlite3 _sqlite;
            private sqlite3_stmt _docQuery;
            private sqlite3_stmt _revQuery;
            private sqlite3_stmt _attQuery;

            public int NumDocs { get; private set; }

            public int NumRevs { get; private set; }

            public v1_upgrader(Database db, string path)
            {
                _db = db;
                _path = path;
            }

            private static Status SqliteErrToStatus(int sqliteErr)
            {
                if (sqliteErr == raw.SQLITE_OK || sqliteErr == raw.SQLITE_DONE) {
                    return new Status(StatusCode.Ok);
                }

                Log.W(TAG, "Upgrade failed: SQLite error {0}", sqliteErr);
                switch (sqliteErr) {
                    case raw.SQLITE_NOTADB:
                        return new Status(StatusCode.BadRequest);
                    case raw.SQLITE_PERM:
                        return new Status(StatusCode.Forbidden);
                    case raw.SQLITE_CORRUPT:
                    case raw.SQLITE_IOERR:
                        return new Status(StatusCode.CorruptError);
                    case raw.SQLITE_CANTOPEN:
                        return new Status(StatusCode.NotFound);
                    default:
                        return new Status(StatusCode.DbError);
                }
            }

            private static int CollateRevIDs(object user_data, string s1, string s2)
            {
                throw new NotImplementedException();
            }

            private Status PrepareSQL(ref sqlite3_stmt stmt, string sql)
            {
                int err;
                if (stmt != null) {
                    err = raw.sqlite3_reset(stmt);
                } else {
                    err = raw.sqlite3_prepare_v2(_sqlite, sql, out stmt);
                }

                if (err != 0) {
                    Log.W("Couldn't compile SQL `{0}` : {1}", sql, raw.sqlite3_errmsg(_sqlite));
                }

                return SqliteErrToStatus(err);
            }

            private Status ImportDoc(string docID, long docNumericID)
            {
                // CREATE TABLE revs (
                //  sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                //  doc_id INTEGER NOT NULL REFERENCES docs(doc_id) ON DELETE CASCADE,
                //  revid TEXT NOT NULL COLLATE REVID,
                //  parent INTEGER REFERENCES revs(sequence) ON DELETE SET NULL,
                //  current BOOLEAN,
                //  deleted BOOLEAN DEFAULT 0,
                //  json BLOB,
                //  no_attachments BOOLEAN,
                //  UNIQUE (doc_id, revid) );

                Status status = PrepareSQL(ref _revQuery, "SELECT sequence, revid, parent, current, deleted, json" +
                                " FROM revs WHERE doc_id=? ORDER BY sequence");
                if (status.IsError) {
                    return status;
                }

                raw.sqlite3_bind_int64(_revQuery, 1, docNumericID);

                var tree = new Dictionary<long, IList<object>>();

                int err;
                while (raw.SQLITE_ROW == (err = raw.sqlite3_step(_revQuery))) {
                    long sequence = raw.sqlite3_column_int64(_revQuery, 0);
                    string revID = raw.sqlite3_column_text(_revQuery, 1);
                    long parentSeq = raw.sqlite3_column_int64(_revQuery, 2);
                    bool current = raw.sqlite3_column_int(_revQuery, 3) != 0;

                    if (current) {
                        // Add a leaf revision:
                        bool deleted = raw.sqlite3_column_int(_revQuery, 4) != 0;
                        IEnumerable<byte> json = raw.sqlite3_column_blob(_revQuery, 5);
                        if (json == null) {
                            json = Encoding.UTF8.GetBytes("{}");
                        }

                        var nuJson = new List<byte>(json);
                        status = AddAttachmentsToSequence(sequence, nuJson);
                        if (status.IsError) {
                            return status;
                        }

                        json = nuJson;
                        RevisionInternal rev = new RevisionInternal(docID, revID, deleted);
                        rev.SetJson(json);

                        var history = new List<string>();
                        history.Add(revID);
                        while (parentSeq > 0) {
                            var ancestor = tree.Get(parentSeq);
                            Debug.Assert(ancestor != null, String.Format("Couldn't find parent sequence of {0} (doc {1})", parentSeq, docID));
                            history.Add((string)ancestor[0]);
                            parentSeq = (long)ancestor[1];
                        }

                        Log.D(TAG, "Upgrading doc {0} history {1}", rev, history);
                        try {
                            _db.ForceInsert(rev, history, null, status);
                        } catch (CouchbaseLiteException e) {
                            status = e.GetCBLStatus();
                        }

                        if (status.IsError) {
                            return status;
                        }

                        NumRevs++;
                    } else {
                        tree[sequence] = new List<object> { revID, parentSeq };
                    }
                }

                ++NumDocs;
                return SqliteErrToStatus(err);
            }

            private Status AddAttachmentsToSequence(long sequence, List<byte> json)
            {
                // CREATE TABLE attachments (
                //  sequence INTEGER NOT NULL REFERENCES revs(sequence) ON DELETE CASCADE,
                //  filename TEXT NOT NULL,
                //  key BLOB NOT NULL,
                //  type TEXT,
                //  length INTEGER NOT NULL,
                //  revpos INTEGER DEFAULT 0,
                //  encoding INTEGER DEFAULT 0,
                //  encoded_length INTEGER );

                Status status = PrepareSQL(ref _attQuery, "SELECT filename, key, type, length,"
                                + " revpos, encoding, encoded_length FROM attachments WHERE sequence=?");
                if (status.IsError) {
                    return status;
                }

                raw.sqlite3_bind_int64(_attQuery, 1, sequence);

                var attachments = new Dictionary<string, object>();

                int err;
                while (raw.SQLITE_ROW == (err = raw.sqlite3_step(_attQuery))) {
                    string name = raw.sqlite3_column_text(_attQuery, 0);
                    var key = raw.sqlite3_column_blob(_attQuery, 1);
                    string mimeType = raw.sqlite3_column_text(_attQuery, 2);
                    long length = raw.sqlite3_column_int64(_attQuery, 3);
                    int revpos = raw.sqlite3_column_int(_attQuery, 4);
                    int encoding = raw.sqlite3_column_int(_attQuery, 5);
                    long encodedLength = raw.sqlite3_column_int64(_attQuery, 6);

                    if (key.Length != SHA1.Create().HashSize / 8) {
                        return new Status(StatusCode.CorruptError);
                    }

                    var blobKey = new BlobKey(key);
                    var att = new NonNullDictionary<string, object> {
                        { "type", mimeType },
                        { "digest", blobKey.Base64Digest() },
                        { "length", length },
                        { "revpos", revpos },
                        { "follows", true },
                        { "encoding", encoding != 0 ? "gzip" : null },
                        { "encoded_length", encoding != 0 ? (object)encodedLength : null }
                    };

                    attachments[name] = att;
                }

                if (err != raw.SQLITE_DONE) {
                    return SqliteErrToStatus(err);
                }

                if (attachments.Count > 0) {
                    // Splice attachment JSON into the document JSON:
                    var attJson = Manager.GetObjectMapper().WriteValueAsBytes(new Dictionary<string, object> { { "_attachments", attachments } });

                    if (json.Count > 2) {
                        json.Insert(json.Count - 1, (byte)',');
                    }

                    json.InsertRange(json.Count - 1, attJson.Skip(1).Take(attJson.Count() - 2));
                }

                return new Status(StatusCode.Ok);
            }

            private Status ImportLocalDocs()
            {
                // CREATE TABLE localdocs (
                //  docid TEXT UNIQUE NOT NULL,
                //  revid TEXT NOT NULL COLLATE REVID,
                //  json BLOB );

                sqlite3_stmt localQuery = null;
                Status status = PrepareSQL(ref localQuery, "SELECT docid, json FROM localdocs");
                if (status.IsError) {
                    return status;
                }

                int err;
                while (raw.SQLITE_ROW == (err = raw.sqlite3_step(localQuery))) {
                    string docID = raw.sqlite3_column_text(localQuery, 0);
                    var data = raw.sqlite3_column_blob(localQuery, 1);
                    IDictionary<string, object> props = null;
                    try {
                        props = Manager.GetObjectMapper().ReadValue<IDictionary<string, object>>(data);
                    } catch (CouchbaseLiteException) {
                    }

                    Log.D(TAG, "Upgrading local doc '{0}'", docID);
                    if (props != null) {
                        try {
                            _db.PutLocalDocument(docID, props);
                        } catch(CouchbaseLiteException e) {
                            Log.W(TAG, "Couldn't import local doc '{0}': {1}", docID, e.GetCBLStatus());
                        }
                    }
                }

                raw.sqlite3_finalize(localQuery);
                return SqliteErrToStatus(err);
            }

            private Status ImportInfo()
            {
                //TODO: Revisit this once pluggable storage is finished
                // CREATE TABLE info (key TEXT PRIMARY KEY, value TEXT);
                sqlite3_stmt infoQuery = null;
                var status = PrepareSQL(ref infoQuery, "SELECT key, value FROM info");
                if (status.IsError) {
                    return status;
                }

                int err = raw.sqlite3_step(infoQuery);
                if (err != raw.SQLITE_ROW) {
                    return SqliteErrToStatus(err);
                }

                string privateUUID = null, publicUUID = null;
                var key = raw.sqlite3_column_text(infoQuery, 0);
                var val = raw.sqlite3_column_text(infoQuery, 1);
                if (key.Equals("privateUUID")) {
                    privateUUID = val;
                } else if (key.Equals("publicUUID")) {
                    publicUUID = val;
                }

                err = raw.sqlite3_step(infoQuery);
                if (err != raw.SQLITE_ROW) {
                    return SqliteErrToStatus(err);
                }

                key = raw.sqlite3_column_text(infoQuery, 0);
                val = raw.sqlite3_column_text(infoQuery, 1);
                if (key.Equals("privateUUID")) {
                    privateUUID = val;
                } else if (key.Equals("publicUUID")) {
                    publicUUID = val;
                }

                if (publicUUID == null || privateUUID == null) {
                    return new Status(StatusCode.CorruptError);
                }

                if (!_db.ReplaceUUIDs(privateUUID, publicUUID)) {
                    return new Status(StatusCode.DbError);
                }

                return new Status(StatusCode.Ok);
            }

            #region IDatabaseUpgrader

            public Status Import()
            {
                // Open source (SQLite) database:
                var err = raw.sqlite3_open_v2(new Uri(_path).AbsolutePath, out _sqlite, raw.SQLITE_OPEN_READONLY, null);
                if (err > 0) {
                    return SqliteErrToStatus(err);
                }

                raw.sqlite3_create_collation(_sqlite, "REVID", raw.SQLITE_UTF8, CollateRevIDs);

                // Open destination database:
                if (!_db.Open()) {
                    Log.W(TAG, "Upgrade failed: Couldn't open new db");
                    return new Status(StatusCode.DbError);
                }

                // Upgrade documents:
                // CREATE TABLE docs (doc_id INTEGER PRIMARY KEY, docid TEXT UNIQUE NOT NULL);
                Status status = PrepareSQL(ref _docQuery, "SELECT doc_id, docid FROM docs");
                if (status.IsError) {
                    return status;
                }

                _db.RunInTransaction(() =>
                {
                    int transactionErr;
                    while(raw.SQLITE_ROW == (transactionErr = raw.sqlite3_step(_docQuery))) {
                        long docNumericID = raw.sqlite3_column_int64(_docQuery, 0);
                        string docID = raw.sqlite3_column_text(_docQuery, 1);
                        Status transactionStatus = ImportDoc(docID, docNumericID);
                        if(transactionStatus.IsError) {
                            status = transactionStatus;
                            return false;
                        }
                    }

                    status = SqliteErrToStatus(transactionErr);
                    return transactionErr == raw.SQLITE_DONE;
                });

                if (status.IsError) {
                    return status;
                }

                status = ImportLocalDocs();
                if (status.IsError) {
                    return status;
                }

                status = ImportInfo();
                return status;
            }

            public void Backout()
            {
                // no-op
            }

            #endregion
        }
    }
}

