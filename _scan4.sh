#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
for f in ./05/057f1dc15927690ee538632cd2434ed78e013e49.svn-base ./08/089f903cdd4e9ab4155b7c72820b338a7202b83e.svn-base ./14/14d9f2238575183f992302067bbdbed3f2b8e140.svn-base ./17/1706b0a54ca9bf088b718de873f863f38c61d53d.svn-base ./1b/1b19c8893db6d4d8538a4dc9fd3e3a1446fc90a6.svn-base; do
  echo "===== $f ====="
  head -c 1200 "$f"
  echo
  echo
done
