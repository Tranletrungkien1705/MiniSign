#!/bin/bash
# Inspect a pristine file: line count + first public method signature
for f in "$@"; do
  p="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine/$f.svn-base"
  echo "== $f  lines=$(grep -c '' "$p")"
  grep -m3 -oE 'public [A-Za-z0-9_<>]+ [A-Za-z0-9_]+\(' "$p"
done
