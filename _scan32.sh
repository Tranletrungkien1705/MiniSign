#!/bin/bash
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "=== files defining RSAUtil ==="
grep -rl 'class RSAUtil' "$ROOT" 2>/dev/null | while read f; do
  if ! head -c 2 "$f" | grep -q 'MZ'; then echo "$f"; fi
done
echo "=== files defining Base64Utils ==="
grep -rl 'class Base64Utils' "$ROOT" 2>/dev/null | while read f; do
  if ! head -c 2 "$f" | grep -q 'MZ'; then echo "$f"; fi
done
