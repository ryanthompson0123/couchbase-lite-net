﻿// 
// IIndex.cs
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
namespace Couchbase.Lite.Query
{
    public interface IIndex
    {
        
    }

    public interface IValueIndex : IIndex
    {
        
    }

    public interface IFTSIndex : IIndex
    {
        IFTSIndex IgnoreAccents(bool ignoreAccents);

        IFTSIndex SetLocale(string localeCode);
    }

    public interface IValueIndexOn : IValueIndex
    {
        IValueIndex On(params IValueIndexItem[] items);
    }

    public interface IFTSIndexOn : IFTSIndex
    {
        IFTSIndex On(params IFTSIndexItem[] items);
    }

    public interface IValueIndexItem
    {
        
    }

    public interface IFTSIndexItem
    {
        
    }
}