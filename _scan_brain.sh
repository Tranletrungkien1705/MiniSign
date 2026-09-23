#!/bin/bash
for f in $(grep -l 'brain' D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine/*/*.svn-base); do
  echo "=== $f ==="
  head -c 300 "$f"
  echo
done