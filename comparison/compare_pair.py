import xml.etree.ElementTree as E,json
class Doc:
 def __init__(self,path):
  self.root=E.parse(path);self.ids={e.get('xs.id'):e for e in self.root.iter() if e.get('xs.id')}
  self.parameters={self.guid(e.find('CParameterGuid')):self.res(e.find('CParameterId')).get('idstr') for e in self.root.iter('CParameterSource') if len(e)}
 def res(self,e):
  while e is not None and e.get('xs.ref'):e=self.ids[e.get('xs.ref')]
  return e
 def guid(self,e):return self.res(e).get('uuid')
 def field(self,e,name):return self.res(next(x for x in e if x.get('xs.n')==name))
 def meshes(self):return {self.res(next(e.iter('CDrawableId'))).get('idstr'):e for e in self.root.iter('CArtMeshSource') if len(e)}
 def keyed(self,e):
  forms={self.guid(next(self.res(f).iter('CFormGuid'))):self.res(f) for f in self.field(e,'keyforms')}
  grid=self.res(next(e.iter('KeyformGridSource')));result={}
  for k in self.field(grid,'keyformsOnGrid'):
   key=[]
   for axis in k.iter('KeyOnParameter'):
    binding=self.field(axis,'binding');p=self.parameters[self.guid(self.field(binding,'parameterGuid'))];idx=int(self.field(axis,'keyIndex').text);v=float(self.res(list(self.field(binding,'keys'))[idx]).text);key.append((p,v))
   f=forms[self.guid(self.field(k,'keyformGuid'))];result[tuple(sorted(key))]=[float(x) for x in self.field(f,'positions').text.split()]
  return result

a,b=Doc('comparison/1/main.xml'),Doc('comparison/2/main.xml');ma,mb=a.meshes(),b.meshes();result={'original_meshes':len(ma),'recovered_meshes':len(mb),'missing_meshes':sorted(set(ma)-set(mb)),'meshes':[]}
for id,mesh in mb.items():
 x,y=a.keyed(ma[id]),b.keyed(mesh)
 err=max((max(abs(i-j) for i,j in zip(x[k],y[k])) for k in x.keys()&y.keys()),default=0)
 result['meshes'].append({'id':id,'key_combinations_equal':x.keys()==y.keys(),'max_same_key_position_error':err,'triangles_equal':a.field(ma[id],'indices').text.split()==b.field(mesh,'indices').text.split(),'keyforms':len(y)})
open('comparison/pair-analysis.json','w').write(json.dumps(result,indent=2))
print('Missing meshes',result['missing_meshes']);print('All grid combinations equal',all(x['key_combinations_equal'] for x in result['meshes']));print('Max coordinate error',max(x['max_same_key_position_error'] for x in result['meshes']));print('All topology equal',all(x['triangles_equal'] for x in result['meshes']));print('Total keyforms',sum(x['keyforms'] for x in result['meshes']))
