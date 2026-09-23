#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== C# files mentioning Sign/Cert/ChuKy (class names) ==="
grep -rli 'class .*Sign\|class .*Cert\|ChuKy\|ChungThu\|KySo\|DigitalSign\|X509' . 2>/dev/null | head -40
