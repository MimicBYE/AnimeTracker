function Test-LinuxPackage {
    param([string]$Path,[string]$BaseVersion,[string]$PackageVersion,[string]$Channel,[string]$Commit)
    Add-Type -AssemblyName System.Formats.Tar
    $prefix="AnimeTracker-$PackageVersion-linux-x64/"
    $blocked='(^|/)(data|covers|imports|bin|obj|artifacts|\.git|\.tools)(/|$)|(^|/)(library|watch-choices|widget-settings|appearance-settings|view-settings|personal-order|arcs)\.xml$|(^|/)\.env($|\.)|\.(pfx|p12|pem|key|pdb|tmp|bak)$'
    $file=[IO.File]::OpenRead($Path);$gzip=[IO.Compression.GZipStream]::new($file,[IO.Compression.CompressionMode]::Decompress);$tar=[System.Formats.Tar.TarReader]::new($gzip)
    $found=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    try {
        while($entry=$tar.GetNextEntry()) {
            $name=$entry.Name
            if($name -match $blocked -or $name -match '(^|/)\.\.(/|$)' -or $name.Contains('\') -or $name.Contains(':') -or !$name.StartsWith($prefix,[StringComparison]::Ordinal) -or $entry.EntryType.ToString() -notin @('RegularFile','V7RegularFile','Directory')){throw "Unsafe Linux archive entry: $name"}
            if(!$found.Add($name)){throw 'Duplicate Linux archive entry.'}
            if($entry.EntryType.ToString() -eq 'Directory'){continue}
            if($name -eq ($prefix+'AnimeTracker')) {
                if(([int]$entry.Mode -band 511) -ne 493){throw 'Linux apphost lost executable mode 0755.'}
                $header=[byte[]]::new(20);$read=0
                while($read -lt 20){$count=$entry.DataStream.Read($header,$read,20-$read);if(!$count){throw 'Truncated ELF apphost.'};$read+=$count}
                if($header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or $header[4] -ne 2 -or $header[5] -ne 1 -or $header[18] -ne 62 -or $header[19] -ne 0){throw 'Linux apphost must be ELF64 x86-64 little-endian.'}
            } elseif($name -eq ($prefix+'BUILD-INFO.json')) {
                if($entry.Length -gt 16384){throw 'Oversized build metadata.'}
                $reader=[IO.StreamReader]::new($entry.DataStream,[Text.Encoding]::UTF8,$true,1024,$true);try{$info=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
                if($info.applicationVersion -ne $BaseVersion -or $info.packageVersion -ne $PackageVersion -or $info.channel -ne $Channel -or $info.commit -ne $Commit){throw 'Linux artifact provenance mismatch.'}
            }
            if($entry.DataStream){$entry.DataStream.CopyTo([IO.Stream]::Null)}
        }
        foreach($name in @('AnimeTracker','AnimeTracker.dll','AnimeTracker.runtimeconfig.json','BUILD-INFO.json','LINUX.md','app.png','THIRD-PARTY-NOTICES.md')){if(!$found.Contains($prefix+$name)){throw "Missing Linux package entry: $name"}}
    } finally {$tar.Dispose();$gzip.Dispose();$file.Dispose()}
}
Export-ModuleMember -Function Test-LinuxPackage
