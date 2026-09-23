#!/bin/bash
# Targeted scan of Dev20 pristine store for signing business logic
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "=== files mentioning signing terms (text only) ==="
grep -rli 'ChungThuSo\|ChuKySo\|KySoDienTu\|DigitalSignature\|X509Certificate\|SignProvider\|SignManager\|SignService\|SignController\|KySo\|ChuKy' "$ROOT" 2>/dev/null | while read f; do
  if ! head -c 2 "$f" | grep -q 'MZ'; then echo "$f"; fi
done | head -60
