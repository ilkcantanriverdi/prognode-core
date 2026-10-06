using System.Security.Cryptography;
using Prognode.Core.Connectivity;
var der=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","leaf.der"));
var id=Guid.Parse("12345678-1234-5678-90ab-1234567890ab");
const string expected="BQF4-2T1V-R08H";
void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name); }
Check(ManualPairingControlCode.FromCertificate(id,der)==expected,"cross-platform DER fixture golden SAS");
Check(ManualPairingControlCode.FromDigest(id,SHA256.HashData(der))==expected,"digest parity");
Check(ManualPairingControlCode.FromCertificate(Guid.NewGuid(),der)!=expected,"server identity change changes SAS");
var alt=(byte[])der.Clone();alt[^1]^=1;
Check(ManualPairingControlCode.FromCertificate(id,alt)!=expected,"MITM leaf certificate changes SAS");
var dir=Path.Combine(Path.GetTempPath(),"prognode-manual-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try{
 var server=new ServerAccessService(dir,5080,5081,"Manual contract",5443,Convert.ToHexString(SHA256.HashData(der)));
 var code=server.GetPairingCode();Check(code.Length==6 && code.All(char.IsAsciiDigit),"CSPRNG six-digit code");
 Check(server.PairingCodeSecondsRemaining()<=120,"120s OTP TTL");
 var paired=server.Pair("Samsung",code,"ANDROID","device-key","192.168.0.5");
 Check(paired.ClientId!=Guid.Empty,"first OTP consumes to paired client");
 var audit=File.ReadAllText(Path.Combine(dir,"audit","manual-pairing.jsonl"));
 Check(audit.Contains("PAIRED") && audit.Contains(paired.ClientId.ToString("D")) && !audit.Contains("pairingCode") && !audit.Contains("accessToken"),"sanitized device pairing audit");
 try{server.Pair("replay",code,"ANDROID","device-key","192.168.0.5");throw new Exception("replay accepted");}catch(ManualPairingException e)when(e.Code=="PAIR_CODE_EXPIRED"){Console.WriteLine("PASS used code rejected");}
 var next=server.GetPairingCode();
 for(var i=0;i<5;i++)try{server.Pair("wrong","abcdef","ANDROID",null,"192.168.0.6");}catch(ManualPairingException e)when(e.Code=="PAIR_CODE_EXPIRED"){}
 try{server.Pair("wrong",next,"ANDROID",null,"192.168.0.6");throw new Exception("rate-limit bypassed");}catch(ManualPairingException e)when(e.Code=="PAIR_RATE_LIMIT"){Console.WriteLine("PASS five-failure limit");}
 server.CancelManualPairingCode();
 Check(server.PairingCodeSecondsRemaining()==0,"administrator cancels code");
}finally{Directory.Delete(dir,true);}
Console.WriteLine("MANUAL_PAIRING_CONTRACT_PASS");
