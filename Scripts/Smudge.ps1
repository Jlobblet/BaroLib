foreach ($file in $args)
{
    (Get-Content "$file") -replace '<RepositoryUrl>{insert repository url here}<\/RepositoryUrl>', '<RepositoryUrl>D:\.nuget\Local</RepositoryUrl>' | Write-Host
}
