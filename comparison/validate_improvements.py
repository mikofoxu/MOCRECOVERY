import xml.etree.ElementTree as E,json,math
class D:
 def __init__(self,path):
  self.r=E.parse(path);self.ids={e.get('xs.id'):e for e in self.r.iter() if e.get('xs.id')}
  self.params={self.guid(e.find('CParameterGuid')):self.res(e.find('CParameterId')).get('idstr') for e in self.r.iter('CParameterSource') if len(e)}
 def res(self,e):
  while e.get('xs.ref'):e=self.ids[e.get('xs.ref')]
  return e
 def guid(self,e):return self.res(e).get('uuid')
 def physics(self):return {self.res(e.find('CPhysicsSettingId')).get('idstr'):e for e in self.r.iter('CPhysicsSettingsSource') if len(e)}
 def flatten(self,e,path='',out=None):
  if out is None:out={}
  e=self.res(e)
  if e.tag in ['CPhysicsDataGuid','CPhysicsSettingsGuid','CPhysicsSettingId']:return out
  if e.tag=='CParameterGuid':out[path]=self.params[self.guid(e)];return out
  if len(e):
   for i,c in enumerate(e):self.flatten(c,path+'/'+c.tag+':'+c.get('xs.n',str(i)),out)
  else:
   value=e.get('v',e.text or '')
   if e.tag in ['f','i']:value=float(value)
   out[path]=value
  return out
 def groups(self):
  groups=[e for e in self.r.iter('CParameterGroup') if len(e)];names={self.guid(e.find('CParameterGroupGuid')):self.res(e.find('CParameterGroupId')).get('idstr') for e in groups};out={}
  for e in groups:
   gid=names[self.guid(e.find('CParameterGroupGuid'))];members=[]
   for child in e.find("carray_list[@xs.n='_childGuids']"):
    members.append((self.params if child.tag=='CParameterGuid' else names)[self.guid(child)])
   out[gid]={'name':e.find("s[@xs.n='name']").text,'members':sorted(members)}
  return out
old,new=D('comparison/1/main.xml'),D('comparison/improved.xml');a,b=old.physics(),new.physics();assert a.keys()==b.keys();maxerr=0;fields=0
for id in a:
 x,y=old.flatten(a[id]),new.flatten(b[id]);assert x.keys()==y.keys(),id
 for key in x:
  if isinstance(x[key],float):
   err=abs(x[key]-y[key]);maxerr=max(maxerr,err);assert err<1e-5,(id,key,x[key],y[key])
  else:assert x[key]==y[key],(id,key,x[key],y[key])
  fields+=1
assert old.groups()==new.groups(),(old.groups(),new.groups())
print('PASS: all',len(a),'physics setups;',fields,'fields; max numeric delta',maxerr)
print('PASS: root and expression-folder names and memberships match original')
summary={'physics_setups':len(a),'physics_fields_checked':fields,'max_numeric_difference':maxerr,'groups_match':True,'inputs':len(list(new.r.iter('CPhysicsInput'))),'outputs':len(list(new.r.iter('CPhysicsOutput'))),'particles':len(list(new.r.iter('CPhysicsVertex')))}
open('comparison/improvement-validation.json','w').write(json.dumps(summary,indent=2))
