#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine || exit 1
for f in ./03/03acb5e8aa04de1bb500b3cc2f7947debc97f771.svn-base ./08/082d51d3dd9f743ac1fbc6ee3dcdd138ae3f0f92.svn-base ./09/098ecf6831c31c5cc2b620564eb6993bef60f9aa.svn-base ./0a/0a68379d03dc932739bbf6755de73bdde673af32.svn-base ./13/1358c9caaadb4b63d0507b21cf770c2f3d5afb5c.svn-base ./18/1873c04e381b1b3309620098d0460d833bdcc77c.svn-base; do
  echo "===== $f ====="
  head -c 700 "$f"
  echo
  echo
done
