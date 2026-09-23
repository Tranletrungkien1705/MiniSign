#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine"
echo "=== text files referencing SignConfig ==="
grep -rl 'SignConfig' "$BASE" 2>/dev/null | while read f; do
  if file "$f" | grep -q text; then echo "$f"; fi
done | head
echo "=== text files referencing RSAUtil ==="
grep -rl 'RSAUtil' "$BASE" 2>/dev/null | while read f; do
  if file "$f" | grep -q text; then echo "$f"; fi
done | head
echo "=== text files referencing SignData ==="
grep -rl 'SignData' "$BASE" 2>/dev/null | while read f; do
  if file "$f" | grep -q text; then echo "$f"; fi
done | head
