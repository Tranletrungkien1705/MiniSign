#!/bin/bash
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
# find text files that reference SignData and show the matching lines
for f in $(grep -rl 'SignData' "$ROOT" 2>/dev/null); do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    echo "===== $f ====="
    grep -n 'SignData\|Signer\|KySo\|ChuKy\|Sign(' "$f" | head -20
  fi
done
