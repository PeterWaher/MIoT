function ToggleOutput()
{
    var Span = document.getElementById("OutputState");
    var CurrentState = Span.innerHTML;
    var xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function ()
    {
        if (xhttp.readyState == 4)
        {
            if (xhttp.status == 200)
            {
                var Data = JSON.parse(xhttp.responseText);
                Span.innerHTML = Data.output ? "ON" : "OFF";
            }

            delete xhttp;
        }
    };

    xhttp.open("POST", "/Set", true);
    xhttp.setRequestHeader("Accept", "application/json");
    xhttp.setRequestHeader("Content-Type", "text/plain");
    if (CurrentState == "ON")
        xhttp.send("OFF");
    else
        xhttp.send("ON");
}
