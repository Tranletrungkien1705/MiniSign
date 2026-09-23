#!/bin/bash
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
for f in 14/14d9f2238575183f992302067bbdbed3f2b8e140.svn-base 37/372ef534f25c510ebf582a12030f9028296861fd.svn-base 52/529c10f6e9c86b88e105319260a8d8b20915b845.svn-base 54/54a6743781fd4ceb720331fce92f16186931192d.svn-base 65/65c8a0b12a1a6942edd84b975c31463868a7a825.svn-base 7f/7fab437314e86e5ddd42b7ffa74a606716ed7e25.svn-base 86/86a8570eacc6aa8399dba3ad5480576e9b05dcb3.svn-base 90/90bab5bb5e9398aa8eb26a4ba002f72832204d77.svn-base a3/a306fbd1ef58cdee399233d07da2be6255e4555e.svn-base; do
  echo "===== $f ====="
  head -c 300 "$ROOT/$f" | tr -d '\0'
  echo ""
  echo "--- lines: $(wc -l < "$ROOT/$f") ---"
done
