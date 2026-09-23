#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine || exit 1
echo "=== Dev20: files mentioning signing terms ==="
grep -rli 'DigitalSignature\|ChuKySo\|ChungThuSo\|KySoDienTu\|SignService\|SignController\|SignProvider\|SignManager\|SignModel\|SignDto\|SignRequest\|SignResponse\|X509Certificate\|RSACryptoServiceProvider' . 2>/dev/null | head -50
