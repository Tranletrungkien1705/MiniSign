#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
for f in ./45/45eb4cf63cf232b9f1d1ba3d6ac420fc72312fde.svn-base ./52/52f6b059a6de0b140b14725fd80a18b326b55588.svn-base ./58/58432bd68832cf988d7b67e05e7081e3f36af9cf.svn-base ./66/662289bee905f758a242249624959a93dc6912ec.svn-base ./77/77bc20486a472e47ec78d32b531674b5641e093a.svn-base ./b4/b41e9a648ee2d989225413df1598ac3d06f1bbac.svn-base ./bf/bf296dc724118be03b01b79577ffc6cdcae000f7.svn-base ./ca/cad33183ce6dcb23543ab16e88524fb694ead5dc.svn-base; do
  echo "===== $f ====="
  grep -in 'sign\|cert\|chuki\|chungthu\|kyso' "$f" | head -20
  echo
done
