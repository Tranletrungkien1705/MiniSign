#!/bin/bash
# List pristine files containing a pattern, with a sample match
PAT="$1"
grep -rl "$PAT" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine | while read -r f; do
  echo "== $f"
  grep -m1 -o "$PAT" "$f"
done
