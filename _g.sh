#!/bin/bash
# usage: _g.sh <pattern>  -> list files + first line
pat="$1"
grep -rln "$pat" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine 2>/dev/null | while read f; do
  echo "== $f"
  head -3 "$f"
done
