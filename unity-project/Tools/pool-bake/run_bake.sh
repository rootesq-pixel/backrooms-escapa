#!/bin/bash
# v7.1.1: POOL_OUT = carpeta de salida; CYCLES_DEVICE=GPU para hornear en GPU (por defecto CPU)
export POOL_OUT=${POOL_OUT:-/workspace/pool-render/fixed}; cd "$(dirname "$0")"
( time blender -b -t 8 -P build_fixed.py -- ${1:-1024} ${2:-2048} ) > $POOL_OUT/bake.log 2>&1
/workspace/toolchain/oidnenv/bin/python denoise.py >> $POOL_OUT/bake.log 2>&1
blender -b -P writeexr.py >> $POOL_OUT/bake.log 2>&1
python3 -c "
import numpy as np; from PIL import Image
a=np.load('$POOL_OUT/lm_dn.npy'); Image.fromarray((np.clip((a/(1+a))**(1/2.2),0,1)*255).astype('uint8')[::-1]).save('$POOL_OUT/lm_preview.png')"
echo BAKEDONE >> $POOL_OUT/bake.log
