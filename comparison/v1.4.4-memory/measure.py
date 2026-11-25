import subprocess,resource,time,json,sys
start=time.monotonic()
with open(sys.argv[1]+'.log','w') as f: p=subprocess.run(sys.argv[2:],stdout=f,stderr=subprocess.STDOUT)
result=dict(exit=p.returncode,seconds=round(time.monotonic()-start,2),peak_rss_kib=resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss)
open(sys.argv[1]+'.json','w').write(json.dumps(result,indent=2))
print(result)
sys.exit(p.returncode)
