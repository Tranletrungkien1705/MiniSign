#!/bin/bash
# Inspect candidate signature source files in Dev pristine store
FILES="1b/1bc0cf110f7abf7d46062e29ec3a44c42e579652.svn-base
1c/1ce702d49090f74b6029b8a58e8dbd550e4c6f89.svn-base
2e/2eb5d638d57ee744d006237b5200311e4732bc78.svn-base
40/40645c1f16c441f1fe77ce8ae9e20d1f5f6375ff.svn-base
69/69d9dd26d9371da2df01f0d58050b8526a6e0762.svn-base
6e/6e807a95d95b498679c9ddc551e5a7eb6a28a7f4.svn-base
b0/b030942b2f5ac6761276464481bbc5b6019e3bd7.svn-base
b6/b602d19cbeff6fe1893a847b6c9f3f60625a8516.svn-base
b9/b997f6a247adf73e957da96f304186539cebfd06.svn-base
e3/e311ffcec04c647913500896c6b132fec477fc7b.svn-base"
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine
for f in $FILES; do
  echo "===== $f ====="
  head -c 200 "$ROOT/$f" | tr -d '\0'
  echo ""
  echo "--- lines: $(wc -l < "$ROOT/$f") ---"
done
