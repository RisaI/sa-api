sc create SAApiService binPath= %~dp0sa-api
sc failure SAApiService actions= restart/60000/restart/60000/""/60000 reset= 86400
sc start SAApiService
sc config SAApiService start=auto