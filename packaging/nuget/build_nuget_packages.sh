#/bin/sh

if [ -z "$1" ]; then
    echo "Usage: ./build_nuget_packages.sh VERSION"
    exit 1
fi

VERSION=$1
FORESTDB_DEPS_URL=https://github.com/couchbaselabs/cbforest/releases/download/1.3-net/1.3-CBForest-Interop.zip
FORESTDB_DEPS_DIR=src/StorageEngines/ForestDB/CBForest/CSharp/prebuilt/

#Make sure submodules are up to date
git submodule update --init --recursive

#Clean staging directory
rm -rf staging

#Download and unzip ForestDB deps to the proper directory
curl $FORESTDB_DEPS_URL -o forestdb.zip
unzip -o forestdb.zip -d $FORESTDB_DEPS_DIR
rm forestdb.zip

#Build the managed DLLs
xbuild /p:Configuration=Packaging src/Couchbase.Lite.Net45.sln
xbuild /p:Configuration=Packaging src/Couchbase.Lite.iOS.sln
xbuild /p:Configuration=Packaging src/Couchbase.Lite.Android.sln

#Do the actual packing
nuget pack -BasePath ./ packaging/nuget/couchbase-lite.nuspec -properties version=$VERSION
nuget pack -BasePath ./ packaging/nuget/couchbase-lite-storage-systemsqlite.nuspec -properties version=$VERSION
nuget pack -BasePath ./ packaging/nuget/couchbase-lite-storage-sqlcipher.nuspec -properties version=$VERSION
nuget pack -BasePath ./ packaging/nuget/couchbase-lite-storage-forestdb.nuspec -properties version=$VERSION