#!/bin/bash
# Scan SVN pristine store for digital-signature related source files
for ROOT in D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine; do
  echo "===== ROOT: $ROOT ====="
  for f in $(grep -rli 'ChungThuSo\|ChuKySo\|KySoDienTu\|DigitalSignature\|X509Certificate\|SignProvider\|SignManager\|SignService\|SignController' "$ROOT" 2>/dev/null); do
    if ! head -c 2 "$f" | grep -q 'MZ'; then
      echo "$f"
    fi
  done | head -80
done
