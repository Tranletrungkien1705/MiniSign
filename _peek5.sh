#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn"
echo "=== Skycic / Sign / Cert paths ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | grep -iE 'skycic|/sign|signdata|signconfig|rsautil|signature|certificate|token|hsm' | grep -v 'HelpPage' | head -60
echo "=== all top-level project dirs ==="
grep -a -o 'idn\.[A-Za-z0-9_.]*' "$BASE/wc.db" | sort -u | head -40
