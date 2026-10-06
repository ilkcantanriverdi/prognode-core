"""Offline SQLite DDL/transaction regression for PROGNODE RC6.4.6; not a .NET build test."""
from pathlib import Path
import tempfile
import sqlite3
import re
import uuid
from datetime import datetime, timezone
src=Path(__file__).resolve().parents[1]/'src/Prognode.Data/Sqlite/DatabaseInitializer.cs'
s=src.read_text(encoding='utf-8')
sqls=re.findall(r'await Exec\(connection, """\n(.*?)\n\s*""", ct\);',s,re.S)
assert len(sqls)>=2, 'Could not locate initializer raw SQL blocks'
fresh_sql, notification_sql=sqls[0],sqls[1]
with tempfile.TemporaryDirectory() as td:
 path=Path(td)/'db.sqlite'
 c=sqlite3.connect(path)
 c.execute('PRAGMA foreign_keys=ON')
 # RC6.2/6.3/6.4.4-style existing database, before v9 tables/occurrence_id.
 c.executescript('''
 CREATE TABLE schema_version(version INTEGER NOT NULL);
 INSERT INTO schema_version VALUES(8);
 CREATE TABLE alarm_events(
 id INTEGER PRIMARY KEY AUTOINCREMENT,alarm_key TEXT NOT NULL,definition_id TEXT NULL,
 tag_id TEXT NULL,device_id TEXT NULL,is_system INTEGER NOT NULL,source_name TEXT NOT NULL,
 alarm_text TEXT NOT NULL,priority TEXT NOT NULL,event_type TEXT NOT NULL,event_time TEXT NOT NULL,
 batch_id TEXT NULL);
 INSERT INTO alarm_events(alarm_key,is_system,source_name,alarm_text,priority,event_type,event_time)
 VALUES('alarm:old',0,'PLC1','Existing alarm','High','Active','2026-09-26T09:00:00Z');
 ''')
 c.executescript(fresh_sql)
 assert c.execute('select version from schema_version').fetchone()[0]==8
 # Production EnsureColumn() is called after the core DDL; match additive v9 migration.
 c.execute('ALTER TABLE alarm_events ADD COLUMN occurrence_id TEXT NULL')
 c.executescript(notification_sql)
 c.execute('UPDATE schema_version SET version=9 WHERE version<9')
 assert c.execute('select count(*) from alarm_events where alarm_key=?',('alarm:old',)).fetchone()[0]==1
 assert c.execute('select version from schema_version').fetchone()[0]==9
 c.commit()
 print('PASS: v8 alarm retained; new tables + occurrence_id upgraded to v9')
 # Test atomic transaction commit and crash rollback across alarm notification and outbox.
 alarm_key='alarm:new';occ=str(uuid.uuid4());now=datetime.now(timezone.utc).isoformat()
 def txn(will_rollback):
  try:
   c.execute('BEGIN')
   c.execute('''INSERT INTO alarm_events(alarm_key,is_system,source_name,alarm_text,priority,event_type,event_time,occurrence_id)
                VALUES (?,?,?,?,?,?,?,?)''',(alarm_key,0,'PLC1','Temperature high','High','Active',now,occ))
   nid=c.execute('''INSERT INTO notification_events(severity,title,message,created_at,requires_ack,repeat_sequence,
               occurrence_id,event_type) VALUES(?,?,?,?,?,?,?,?) RETURNING id''',('High','Temperature','80C',now,1,0,occ,'ACTIVE')).fetchone()[0]
   c.execute("INSERT INTO notification_outbox(event_id,state,attempts,next_attempt_at) VALUES(?,'QUEUED',0,?)",(nid,now))
   if will_rollback:raise RuntimeError('simulated crash before COMMIT')
   c.commit();return nid
  except RuntimeError:
   c.rollback();return None
 txn(True)
 assert c.execute('SELECT count(*) FROM alarm_events WHERE occurrence_id=?',(occ,)).fetchone()[0]==0
 assert c.execute('SELECT count(*) FROM notification_events').fetchone()[0]==0
 assert c.execute('SELECT count(*) FROM notification_outbox').fetchone()[0]==0
 nid=txn(False)
 assert nid>0
 assert c.execute('select count(*) from notification_outbox where event_id=?',(nid,)).fetchone()[0]==1
 c.commit();c.close()
 c=sqlite3.connect(path)
 assert c.execute('select count(*) from notification_events where occurrence_id=?',(occ,)).fetchone()[0]==1
 c.close()
 print('PASS: alarm + notification + remote outbox atomic rollback/commit; persisted after DB reopen')
print('TEST NOTE: SQLite SQL contract only; not a Windows .NET/Flutter end-to-end test')
