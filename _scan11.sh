#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== grep for 'Sign' as a word/class in all inos files ==="
grep -rn 'SignService\|SignController\|DigitalSign\|ESign\|eSign\|ChuKySo\|ChuKy\|KySo\|SignProvider\|SignManager\|SignModel\|SignDto\|SignRequest' $(cat /tmp/inos_files.txt) 2>/dev/null | head -40
echo
echo "=== grep for 'CA' / 'USB Token' / 'HSM' / 'PKCS' ==="
grep -rn 'PKCS\|HSM\|UsbToken\|USBToken\|SmartCard\|CryptoAPI\|CspParameters' $(cat /tmp/inos_files.txt) 2>/dev/null | head -30
