#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
# Find text files (not MZ binaries) that mention signing
echo "=== text files mentioning Sign/Cert (excluding MZ binaries) ==="
for f in $(grep -rli 'class .*Sign\|class .*Cert\|X509\|ChuKy\|ChungThu\|KySo\|DigitalSign' . 2>/dev/null); do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    echo "$f"
  fi
done | head -40
