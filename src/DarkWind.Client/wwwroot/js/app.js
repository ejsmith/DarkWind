
function selectText(tbId)
{
    var tb = document.querySelector("#" + tbId);

    if (tb.select)
    {
        tb.focus();
        tb.select();
    }
}

function fitXterm() {
}

