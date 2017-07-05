function RefreshGauge()
{
    var Images = document.getElementsByTagName("IMG");
    Images[0].src = "/Momentary?TP=" + Date();

    var xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function ()
    {
        if (xhttp.readyState == 4)
        {
            if (xhttp.status == 200)
            {
                var Data = JSON.parse(xhttp.responseText);
                var Cells = document.getElementsByTagName("TD");

                Cells[1].firstChild.innerHTML = Data.light.value + Data.light.unit;
                Cells[3].firstChild.innerHTML = Data.movement ? "Detected" : "Not detected";
            }

            delete xhttp;
        }
    };

    xhttp.open("GET", "/Momentary", true);
    xhttp.setRequestHeader("Accept", "application/json");
    xhttp.send("");
}

window.setInterval(RefreshGauge, 2000);