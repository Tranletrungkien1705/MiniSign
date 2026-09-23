#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== sample: first lines of a few matched files ==="
for f in ./02/02014f11b58b8d1ac98e11a3701066ec68061098.svn-base ./03/0352acf1df3904a82c1d36328abdb5645f51cc77.svn-base ./05/057d6836a8ad525c8f19b012fc945f5e6155c7d8.svn-base; do
  echo "----- $f -----"
  head -c 400 "$f"
  echo
done
