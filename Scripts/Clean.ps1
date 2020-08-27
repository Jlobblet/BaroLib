foreach ($file in $args)
{
    (Get-Content "$file") -replace '<RepositoryUrl>D:\\.nuget\\Local</RepositoryUrl>', '<RepositoryUrl>{insert repository url here}</RepositoryUrl>' | Write-Host
}
