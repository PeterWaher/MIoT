Title: History
Author: Peter Waher
Description: This page displays historical values of the sensor.
Cache-Control: max-age=0, no-cache, no-store
CSS: Main.css

Historical values
============================

The following graphs display historical values of the sensor.

Legend
------------

| Color                                    | Meaning                           |
|:----------------------------------------:|:----------------------------------|
| <span style='color:green'>Green</span>   | Average light during period.      |
| <span style='color:orange'>Orange</span> | Momentary light at end of period. |
| <span style='color:red'>Red</span>       | Maximum light during period.      |
| <span style='color:blue'>Blue</span>     | Minimum light during period.      |


Minutes
-------------

{
Records:=SensorHttp.App.GetLastMinutes();
plot2dline(Records.MinLightAt, Records.MinLight, "Blue")+
	plot2dline(Records.MaxLightAt, Records.MaxLight, "Red")+
	plot2dline(Records.Timestamp, Records.Light, "Orange")+
	plot2dline(Records.Timestamp, Records.AvgLight, "Green");
}


Hours
-------------

{
Records:=SensorHttp.App.GetLastHours();
plot2dline(Records.MinLightAt, Records.MinLight, "Blue")+
	plot2dline(Records.MaxLightAt, Records.MaxLight, "Red")+
	plot2dline(Records.Timestamp, Records.Light, "Orange")+
	plot2dline(Records.Timestamp, Records.AvgLight, "Green");
}


Days
-------------

{
Records:=SensorHttp.App.GetLastDays();
plot2dline(Records.MinLightAt, Records.MinLight, "Blue")+
	plot2dline(Records.MaxLightAt, Records.MaxLight, "Red")+
	plot2dline(Records.Timestamp, Records.Light, "Orange")+
	plot2dline(Records.Timestamp, Records.AvgLight, "Green");
}
