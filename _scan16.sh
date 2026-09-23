#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine || exit 1
echo "=== Dev20: sample text files (non-MZ) ==="
cnt=0
for f in $(find . -name '*.svn-base' | head -400); do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    echo "$f : $(head -c 80 "$f" | tr '\n' ' ')"
    cnt=$((cnt+1))
    [ $cnt -ge 25 ] && break
  fi
done
