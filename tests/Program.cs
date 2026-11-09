using PeripheralBattery;
int checks=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks++; }
void Reject(Action action,string name) { try { action(); } catch(InvalidDataException) { checks++; return; } throw new Exception("Accepted invalid data: "+name); }
var pair=BatteryProtocol.ParsePairing(Convert.FromHexString("A00001000E1C0305"));
Check(pair.Count==1 && pair[0].ProductId==7182 && pair[0].ControlReport==3,"actual receiver capture");
Check(BatteryProtocol.ParsePairing(Convert.FromHexString("A0000000")).Count==0,"unpaired receiver");
Check(BatteryProtocol.ParsePairing(Convert.FromHexString("A00002000E1C0305651D0204")).Count==1,"ignore paired keyboard");
Reject(()=>BatteryProtocol.ParsePairing(Convert.FromHexString("A00002000E1C0305")),"truncated pairing");
Reject(()=>BatteryProtocol.ParsePairing(Convert.FromHexString("A000FF7F")),"invalid count");
Reject(()=>BatteryProtocol.ParsePairing(Convert.FromHexString("A1000000")),"wrong command");
var power=BatteryProtocol.ParsePower(Convert.FromHexString("120700005C0214E40F000001"));
Check(power.Percent==92 && !power.Charging && power.Millivolts==4068,"actual battery capture");
Check(BatteryProtocol.ParsePower(Convert.FromHexString("12070000640214E40F010001")).Charging,"charging");
Check(BatteryProtocol.ParsePower(Convert.FromHexString("12070000000214E40F000001")).Percent==0,"empty battery");
Reject(()=>BatteryProtocol.ParsePower(Convert.FromHexString("12070000FF0214E40F000001")),"offline sentinel");
Reject(()=>BatteryProtocol.ParsePower(Convert.FromHexString("120700005C0214E40F020001")),"bad charging flag");
Reject(()=>BatteryProtocol.ParsePower(Convert.FromHexString("120700005C0214E40F")),"truncated battery");
Reject(()=>BatteryProtocol.ParsePower(Convert.FromHexString("120100005C0214E40F000001")),"keyboard response");
var cherryRequest=CherryProtocol.PowerRequest();
Check(cherryRequest.Length==64 && cherryRequest[0]==4 && cherryRequest[3]==0x1a && cherryRequest[4]==6 && cherryRequest[5]==0 && cherryRequest[6]==0,"read only power command");
Check(cherryRequest.Skip(3).Sum(v=>(int)v)==(cherryRequest[1]|cherryRequest[2]<<8),"CHERRY request checksum");
var cherryResponse=Convert.FromHexString("0420001A060000004E01000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
var cherryPower=CherryProtocol.ParsePower(cherryResponse);
Check(cherryPower.Percent==78 && cherryPower.Charging,"CHERRY actual MX 10.1 capture, confirmed on keyboard display");
cherryResponse[9]=0; Check(!CherryProtocol.ParsePower(cherryResponse).Charging,"CHERRY not charging");
cherryResponse[9]=2; Check(CherryProtocol.ParsePower(cherryResponse).Charging,"CHERRY charge state 2");
cherryResponse[8]=100; Check(CherryProtocol.ParsePower(cherryResponse).Percent==100,"CHERRY full battery");
cherryResponse[8]=0; Check(CherryProtocol.ParsePower(cherryResponse).Percent==0,"CHERRY empty battery");
cherryResponse[8]=255; Reject(()=>CherryProtocol.ParsePower(cherryResponse),"CHERRY invalid battery");
cherryResponse[8]=78; cherryResponse[9]=255; Reject(()=>CherryProtocol.ParsePower(cherryResponse),"CHERRY invalid charge state");
cherryResponse[9]=0; cherryResponse[3]=0x1b; Reject(()=>CherryProtocol.ParsePower(cherryResponse),"unrelated CHERRY command");
cherryResponse[3]=0x1a; Reject(()=>CherryProtocol.ParsePower(cherryResponse[..12]),"truncated CHERRY report");
cherryResponse[7]=255;
try { CherryProtocol.ParsePower(cherryResponse); throw new Exception("Accepted CHERRY device error"); } catch(IOException) { checks++; }
Check(!CherryProtocol.IsPowerResponse(Convert.FromHexString("143F3F000000000000000000000000000000000000")),"unrelated feature report is not battery data");
Console.WriteLine($"PASS: {checks} protocol checks");
