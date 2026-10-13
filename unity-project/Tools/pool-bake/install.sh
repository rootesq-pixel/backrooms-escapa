#!/bin/bash
F=/workspace/pool-render/fixed; D=/workspace/BackroomsEscape/Assets/Resources/PoolRoom
cp $F/PoolRoomFixed.bytes $D/PoolRoomFixed.bytes; cp $F/PoolRoom_LM.exr $D/PoolRoom_LM.exr
python3 -c "
import json; j=json.load(open('$F/probes.json'))
for p in j['probes']: p['s']=[round(v,6) for s in p['s'] for v in s]
json.dump(j,open('$D/PoolRoomProbes.json','w'),separators=(',',':'))"
ls -la $D
