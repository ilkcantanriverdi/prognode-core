"""HF3+ SQL smoke test: run the exact embedded SQL against in-memory SQLite."""
from pathlib import Path
import re, sqlite3, math
src=(Path(__file__).resolve().parents[1]/'src/Prognode.Data/Sqlite/SqliteHistorianRepository.cs').read_text()
segment=src[src.index('private static async Task<IReadOnlyList<TrendPoint>> QueryBucketedPointsAsync'):]
sql=re.search(r'command\.CommandText\s*=\s*"""(.*?)"""',segment,re.S).group(1)
con=sqlite3.connect(':memory:')
con.execute('CREATE TABLE historian_samples(tag_id TEXT,timestamp_unix_ms INTEGER,value REAL,quality TEXT,batch_id TEXT)')
con.execute('CREATE INDEX idx_tag_time ON historian_samples(tag_id,timestamp_unix_ms)')
rows=[]
for i in range(12000):
 q='Good';v=20+(i%27)*0.1
 if i==3121:v=365.75   # brief high spike must survive
 if i==5133:v=-27.25  # brief negative trough must survive
 if i==8071:q='Bad';v=None
 if i==8072:q='Stale';v=None
 rows.append(('tag1',i*1000,v,q,None))
con.executemany('INSERT INTO historian_samples VALUES(?,?,?,?,?)',rows)
maxpoints=3000;bucketms=math.ceil(11999000/(maxpoints//10))
result=con.execute(sql,{'tagId':'tag1','from':0,'to':11999000,'bucketMs':bucketms}).fetchall()
assert any(abs(v-365.75)<1e-9 for _,v,q in result if v is not None and q=='Good'), 'High spike lost'
assert any(abs(v+27.25)<1e-9 for _,v,q in result if v is not None and q=='Good'), 'Negative trough lost'
assert {q for _,v,q in result}=={'Good','Bad','Stale'}, 'Quality gaps lost'
assert len(result)<maxpoints, ('Unbounded output',len(result))
assert all(t==orig[1] and (v==orig[2] or (v is None and orig[2] is None)) and q==orig[3]
           for t,v,q in result for orig in [rows[t//1000]]), 'Invented aggregate samples'
assert result==sorted(result,key=lambda x:x[0]),'Wrong time ordering'
print(f'PASS: HF3+ bucketed real samples: {len(result)} of {len(rows)}; high/low spikes, BAD/STALE and original timestamps preserved.')
