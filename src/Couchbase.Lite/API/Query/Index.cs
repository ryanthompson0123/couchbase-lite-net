// 
// Index.cs
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

using System;
using Couchbase.Lite.Internal.Query;
using Couchbase.Lite.Util;

namespace Couchbase.Lite.Query
{
    public static class Index
    {
        public static IValueIndexOn Value()
        {
            return new QueryValueIndex();
        }

        public static IFTSIndexOn FTS()
        {
            return new QueryFTSIndex();
        }
    }

    public static class ValueIndexItem
    {
        public static IValueIndexItem Expression(IExpression expression)
        {
            var e = Misc.TryCast<IExpression, QueryExpression>(expression);
            return new QueryValueIndexItem(e);
        }
    }

    public static class FTSIndexItem
    {
        public static IFTSIndexItem Expression(IExpression expression)
        {
            var e = Misc.TryCast<IExpression, QueryExpression>(expression);
            return new QueryFTSIndexItem(e);
        }
    }
}