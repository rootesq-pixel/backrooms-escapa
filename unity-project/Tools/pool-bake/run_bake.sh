#!/bin/bash
cd /workspace/pool-render/fixed
( time blender -b -t 8 -P build_fixed.py -- ${1:-1024} ${2:-2048} ) > bake.log 2>&1
/workspace/toolchain/oidnenv/bin/python denoise.py >> bake.log 2>&1
blender -b -P writeexr.py >> bake.log 2>&1
python3 -c "
import numpy as np; from PIL import Image
a=np.load('lm_dn.npy'); Image.fromarray((np.clip((a/(1+a))**(1/2.2),0,1)*255).astype('uint8')[::-1]).save('lm_preview.png')"
echo BAKEDONE >> bake.log
