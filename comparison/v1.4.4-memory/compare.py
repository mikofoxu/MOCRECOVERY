from pathlib import Path
import zlib,struct,hashlib,json
def inspect(path):
 h=hashlib.sha256();metadata={};decoder=zlib.decompressobj()
 with path.open('rb') as f:
  assert f.read(8)==b'\x89PNG\r\n\x1a\n'
  while True:
   n,tag=struct.unpack('>I4s',f.read(8)); data=f.read(n); crc=struct.unpack('>I',f.read(4))[0]
   assert zlib.crc32(tag+data)==crc
   if tag==b'IDAT':
    out=decoder.decompress(data,65536)
    while out:
     h.update(out);out=decoder.decompress(decoder.unconsumed_tail,65536)
   elif tag==b'IEND':break
   else:metadata[tag.decode()]=hashlib.sha256(data).hexdigest()
 h.update(decoder.flush());assert decoder.eof
 return dict(pixels=h.hexdigest(),metadata=metadata)
files=sorted(Path('memory-test/old-png').glob('*.png'))
for f in files:assert inspect(f)==inspect(Path('memory-test/new-png')/f.name),f.name
print(f'PASS: {len(files)} layers have identical decompressed scanlines, dimensions, ICC metadata; all PNG chunk CRCs valid')
Path('memory-test/pixel-check.txt').write_text(f'{len(files)} layers: identical decompressed scanlines, dimensions and ICC metadata. All PNG chunk CRCs valid.\n')
