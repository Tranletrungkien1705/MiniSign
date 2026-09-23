#!/bin/bash
for f in 54/54a6743781fd4ceb720331fce92f16186931192d 9f/9fda1c76b54e81c76d88fb2a40298c46ca3524a7 bb/bba2661ebefdc88510d901438eb22273349cb643 bf/bf52cec59f2f7c837cea42309418a3c8803832f4; do
  echo "=== $f ==="
  grep -i -m5 'brain' "D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine/$f.svn-base"
done
