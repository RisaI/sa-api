sc create SAApiService binPath= %~dp0sa-api
sc failure MyService actions= restart/60000/restart/60000/""/60000 reset= 86400
sc start MyService
sc config MyService start=auto