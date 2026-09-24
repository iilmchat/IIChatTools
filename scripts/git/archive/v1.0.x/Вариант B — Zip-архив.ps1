cd G:\AI\IIChatTools
# Создаём архив, исключая bin/obj/Data/logs
$temp = "G:\AI\IIChatTools-export"
Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
robocopy "G:\AI\IIChatTools" $temp /E `
    /XD bin obj .git .vs .idea Data logs Workspace `
    /XF *.db *.db-shm *.db-wal *.user *.suo

Compress-Archive -Path "$temp\*" -DestinationPath "IIChatTools-v1.0.2-src.zip" -CompressionLevel Optimal
Remove-Item $temp -Recurse -Force