// 
// QueryIndex.cs
// 
// Author:
//     Jim Borden  <jim.borden@couchbase.com>
// 
// Copyright (c) 2017 Couchbase, Inc All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// 
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Couchbase.Lite.Query;
using Couchbase.Lite.Util;
using LiteCore.Interop;

namespace Couchbase.Lite.Internal.Query
{
    internal abstract class QueryIndex : IIndex
    {
        #region Properties

        internal abstract C4IndexOptions IndexOptions { get; }
        internal abstract C4IndexType IndexType { get; }

        #endregion

        #region Public Methods

        public abstract object EncodeToJSON();

        #endregion
    }

    internal sealed class QueryValueIndex : QueryIndex, IValueIndexOn
    {
        #region Variables

        private IEnumerable<QueryValueIndexItem> _items;

        #endregion

        #region Properties

        internal override C4IndexOptions IndexOptions { get; } = new C4IndexOptions();

        internal override C4IndexType IndexType => C4IndexType.ValueIndex;

        #endregion

        #region Overrides

        public override object EncodeToJSON()
        {
            return QueryExpression.EncodeToJSON(_items.Select(x => x.Expression).ToList());
        }

        #endregion

        #region IValueIndexOn

        public IValueIndex On(params IValueIndexItem[] items)
        {
            items.ThrowIfNullOrEmpty(nameof(items));

            _items = items.Cast<QueryValueIndexItem>();
            return this;
        }

        #endregion
    }

    internal sealed class QueryFTSIndex : QueryIndex, IFTSIndexOn
    {
        #region Constants

        private static readonly string DefaultLanguage = CultureInfo.CurrentCulture.TwoLetterISOLanguageName != "iv"
            ? CultureInfo.CurrentCulture.TwoLetterISOLanguageName
            : "en";

        #endregion

        #region Variables

        private bool _ignoreAccents;

        private IEnumerable<QueryFTSIndexItem> _items;
        private string _localeCode;

        #endregion

        #region Properties

        internal override C4IndexOptions IndexOptions => new C4IndexOptions {
            ignoreDiacritics = _ignoreAccents,
            language = _localeCode ?? DefaultLanguage
        };

        internal override C4IndexType IndexType => C4IndexType.FullTextIndex;

        #endregion

        #region Overrides

        public override object EncodeToJSON()
        {
            return QueryExpression.EncodeToJSON(_items.Select(x => x.Expression).ToList());
        }

        #endregion

        #region IFTSIndex

        public IFTSIndex IgnoreAccents(bool ignoreAccents)
        {
            _ignoreAccents = ignoreAccents;
            return this;
        }

        public IFTSIndex SetLocale(string localeCode)
        {
            _localeCode = localeCode;
            return this;
        }

        #endregion

        #region IFTSIndexOn

        public IFTSIndex On(params IFTSIndexItem[] items)
        {
            items.ThrowIfNullOrEmpty(nameof(items));

            _items = items.Cast<QueryFTSIndexItem>();
            return this;
        }

        #endregion
    }
}