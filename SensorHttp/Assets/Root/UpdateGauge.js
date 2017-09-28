function RefreshGauge()
{
    var Images = document.getElementsByTagName("IMG");
    Images[0].src = "/MomentaryPng?TP=" + Date();   // We can't control Authorization headers, so we need a separate resource that checks the session user variable on the server.

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
                Cells[3].firstChild.innerHTML = Data.motion ? "Detected" : "Not detected";
            }

            delete xhttp;
        }
    };

    xhttp.open("GET", "/Momentary", true);
    xhttp.withCredentials = true;
    xhttp.setRequestHeader("Accept", "application/json");
    xhttp.setRequestHeader("Authorization", "Bearer " + SessionToken);
    xhttp.send("");
}

var SessionToken = null;

function GetSessionToken()
{
    var xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function ()
    {
        if (xhttp.readyState == 4)
        {
            if (xhttp.status == 200)
            {
                SessionToken = xhttp.responseText;
                window.setInterval(RefreshGauge, 2000);
            }

            delete xhttp;
        }
    };

    xhttp.open("POST", "/GetSessionToken", true);
    xhttp.send("");
}

GetSessionToken();
