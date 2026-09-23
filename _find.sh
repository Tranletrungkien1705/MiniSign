#!/bin/bash
# find file containing a pattern and print its size + first lines
pat="$1"
grep -rln "$pat" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine 2>/dev/null | while read f; do
  if file "$f" | grep -qi text; then
    echo "== $f ($(wc -l < "$f") lines)"
  fi
done
