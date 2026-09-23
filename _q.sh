#!/bin/bash
P="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine"
echo "=== files with Invoice_Invoice_ and their sizes ==="
for f in $(grep -rl "Invoice_Invoice_" "$P" 2>/dev/null); do
  sz=$(wc -c < "$f")
  echo "$sz  $f"
done | sort -rn | head -20
