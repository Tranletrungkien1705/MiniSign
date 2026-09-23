#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine || exit 1
echo "=== Dev20: files with 'namespace idn.iNOS' ==="
grep -rl 'namespace idn.iNOS' . 2>/dev/null > /tmp/inos20.txt
wc -l /tmp/inos20.txt
echo "=== Dev20: files mentioning Sign/Cert/ChuKy (text) ==="
grep -rli 'DigitalSignature\|ChuKySo\|ChungThuSo\|KySoDienTu\|SignService\|SignController\|SignProvider\|SignManager' . 2>/dev/null | head -40
