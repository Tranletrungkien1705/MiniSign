#!/bin/bash
P="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine"
echo "===== text files mentioning TTHD / HoaDon / Invoice sign flow ====="
for f in $(grep -rln "TTHD\|HoaDonDienTu\|InvoiceSign\|SignInvoice\|PublishInvoice\|InvoicePublish" "$P" 2>/dev/null); do
  if head -c 4 "$f" | grep -q "MZ"; then continue; fi
  echo "--- $f ($(wc -c < "$f") bytes) ---"
done
echo
echo "===== search 'SignData' model usage across text files ====="
for f in $(grep -rln "SignData" "$P" 2>/dev/null); do
  if head -c 4 "$f" | grep -q "MZ"; then continue; fi
  echo "--- $f ---"
  grep -n "SignData\|CertSerial\|FileName\|Type" "$f" | head -10
done
