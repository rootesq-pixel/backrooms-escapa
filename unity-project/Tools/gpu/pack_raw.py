# blender -b --factory-startup --python pack_raw.py -- <dir>  : lm_raw.npy -> lm_raw16.npz (float16, comprimido) para copiarlo (<100 MB)
import sys, numpy as np, os
d = sys.argv[sys.argv.index('--') + 1]
a = np.load(os.path.join(d, 'lm_raw.npy')).astype(np.float16)
np.savez_compressed(os.path.join(d, 'lm_raw16.npz'), lm=a); print('PACKED', a.shape, os.path.getsize(os.path.join(d, 'lm_raw16.npz')))
