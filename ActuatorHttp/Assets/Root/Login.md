Title: Login
Description: Login page to the device.
Author: Peter Waher
Master: Menu.md
Parameter: from

Login
=============

<form id="LoginForm" action="/Login" method="post">

Please login by providing a user name and password:

User Name:  
<input id="UserName" name="UserName" type="text" autofocus="autofocus" style="width:20em" />

Password:  
<input id="Password" name="Password" type="password" style="width:20em" />

{{if exists(LoginError) then]]
<div class='error'>
((LoginError))
</div>
[[;}}

<button id="LoginButton" type="submit">Login</button>

</form>
