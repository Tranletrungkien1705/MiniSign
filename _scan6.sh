#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
for f in ./17/1776fd957e3108aae68364164c10286eaa4930a0.svn-base ./1b/1bc0cf110f7abf7d46062e29ec3a44c42e579652.svn-base ./1c/1ce702d49090f74b6029b8a58e8dbd550e4c6f89.svn-base ./1d/1df1dc3672a9c124c964f69d5d9cf90926bb6016.svn-base ./2e/2eb5d638d57ee744d006237b5200311e4732bc78.svn-base ./3c/3cfcda05e7cec193c2c48f185f8ae261614a0ea7.svn-base; do
  echo "===== $f ====="
  head -c 900 "$f"
  echo
  echo
done
