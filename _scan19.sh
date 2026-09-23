#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine || exit 1
echo "=== text C# files mentioning signing terms ==="
for f in $(grep -rli 'DigitalSignature\|ChuKySo\|ChungThuSo\|KySoDienTu\|SignService\|SignController\|SignProvider\|SignManager\|SignModel\|SignDto\|SignRequest\|SignResponse\|X509Certificate\|RSACryptoServiceProvider' . 2>/dev/null); do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    echo "$f"
  fi
done | head -50
