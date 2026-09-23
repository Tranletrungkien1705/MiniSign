#!/bin/bash
for f in 0e/0e27ebcf209faf7a650a6520636fa4a71affceee 25/25d5dbefcc76ff3a759eb0b18b924fc2f7e08c8f 37/372ef534f25c510ebf582a12030f9028296861fd 5e/5ea8fca350507017e47246be8982f51cb3c9d03c 85/85758f997778f6405b08602d92408cd1604aae4f a3/a306fbd1ef58cdee399233d07da2be6255e4555e a8/a889b35717fab4ddc3097cba3a3cbe08972ee2f1 e1/e1b1a5096d40d9b1b462c5d0af1d14064b694250 f6/f67a4f5b14f20b5659b4cc4ae8798357d6e7e942; do
  echo "== $f =="
  head -c 200 "D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine/$f.svn-base" | tr -d '\r'
  echo
done
