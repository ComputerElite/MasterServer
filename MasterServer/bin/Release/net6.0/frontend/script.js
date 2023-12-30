function GetTimeDifferenceToNow(timestamp) {
    const difference = (Date.now() - new Date(timestamp).getTime()) / 1000;
    return `${Math.floor(difference / 31449600)} years, ${Math.floor(difference % 31449600 / 2620800)} months, ${Math.floor(difference % 2620800 / 86400)} days, ${Math.floor(difference % 86400 / 3600)} hours, ${Math.floor(difference % 3600 / 60)} minutes and ${Math.floor(difference % 60)} seconds ago`;
}

function openTab(evt, tab) {
    // Declare all variables
    var i, tabcontent, tablinks;
  
    // Get all elements with class="tabcontent" and hide them
    tabcontent = document.getElementsByClassName("tabcontent");
    for (i = 0; i < tabcontent.length; i++) {
      tabcontent[i].style.display = "none";
    }
  
    // Get all elements with class="tablinks" and remove the class "active"
    tablinks = document.getElementsByClassName("tablinks");
    for (i = 0; i < tablinks.length; i++) {
      tablinks[i].className = tablinks[i].className.replace(" active", "");
    }
  
    // Show the current tab, and add an "active" class to the button that opened the tab
    document.getElementById(tab).style.display = "block";
    evt.className += " active";
}


function TextBoxError(id, text) {
    ChangeTextBoxProperty(id, "var(--red)", text)
}

function TextBoxText(id, text) {
    ChangeTextBoxProperty(id, "#03cffc", text)
}

function TextBoxGood(id, text) {
    ChangeTextBoxProperty(id, "var(--interactableColor)", text)
}

function HideTextBox(id) {
    document.getElementById(id).style.visibility = "hidden"
}

function ChangeTextBoxProperty(id, color, innerHtml) {
    var text = document.getElementById(id)
    text.style.visibility = "visible"
    text.style.border = color + " 1px solid"
    text.innerHTML = innerHtml
}

function GetCookie(cookieName) {
    var name = cookieName + "=";
    var ca = document.cookie.split(';');
    for (var i = 0; i < ca.length; i++) {
        var c = ca[i];
        while (c.charAt(0) == ' ') {
            c = c.substring(1);
        }
        if (c.indexOf(name) == 0) {
            return c.substring(name.length, c.length);
        }
    }
    return "";
}

function SetCookie(name, value, expiration) {
    var d = new Date();
    d.setTime(d.getTime() + (expiration * 24 * 60 * 60 * 1000));
    var expires = "expires=" + d.toUTCString();
    document.cookie = name + "=" + value + ";" + expires + ";path=/";
}