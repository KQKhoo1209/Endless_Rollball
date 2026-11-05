# Numeric regressions against the actual chart/simulation sources; does not run Unity.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $nunitPath = (Get-ChildItem 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll' | Select-Object -First 1).FullName
    if (-not $nunitPath) { throw 'Import the Unity project first to restore its NUnit package.' }
    Add-Type -Path $nunitPath
    $files = @('Assets/Scripts/Player/CloudLandingMath.cs','Assets/Scripts/Player/CloudMusicChart.cs',
        'Assets/Scripts/Player/CloudMusicSimulation.cs','Assets/Tests/EditMode/CloudMusicSimulationTests.cs',
        'Assets/Tests/EditMode/CloudLandingMathTests.cs', 'Assets/Scripts/Player/SoundPlaybackClock.cs',
        'Assets/Tests/EditMode/SoundPlaybackClockTests.cs', 'Assets/Scripts/Player/PianoLandingSequence.cs',
        'Assets/Tests/EditMode/PianoLandingSequenceTests.cs', 'Assets/Scripts/Player/CloudSongRotation.cs',
        'Assets/Tests/EditMode/CloudSongRotationTests.cs', 'Assets/Tests/EditMode/CloudMusicEndingTests.cs')
    $code = ($files | ForEach-Object { Get-Content -Raw $_ }) -join "`n"
    $code = $code.Replace('using System;', '').Replace('using NUnit.Framework;', '')
    $code = "using System;`nusing NUnit.Framework;`n" + $code
    Add-Type -TypeDefinition $code -ReferencedAssemblies @($nunitPath,
        (Join-Path $PSHOME 'ref/mscorlib.dll'),(Join-Path $PSHOME 'ref/System.Runtime.dll'))
    $tests = [EndlessRollball.Tests.CloudMusicSimulationTests]::new()
    $tests.ReachableMusicChartBouncesForFull45Seconds(1.53846,9)
    $tests.ReachableMusicChartBouncesForFull45Seconds(.731707,12)
    $tests.ReachableMusicChartBouncesForFull45Seconds(.3125,22)
    $tests.MissesRecoverWithoutSeekingMusicTime()
    $tests.NewRunStartsAtBeginningWithNoOldOutcomes()
    $tests.ImpossibleFastLaneChangeIsRejected()
    $tests.SoundSignalsComeFromLandingsAndMissesOnly()
    $oldTests = [EndlessRollball.Tests.CloudLandingMathTests]::new()
    foreach ($step in @(.01,.02,.0333333)) { $oldTests.RepeatedLandingsDoNotFallThroughAtStepBoundary($step) }
    $oldTests.DescendingContactAtStartOfStepBounces()
    $oldTests.AscendingBallDoesNotBounceAgainImmediately()
    $oldTests.BallWellBelowPlatformIsNotCaught()
    $oldTests.RoundedPlatformRejectsInvisibleSquareCorner()
    Write-Output 'PASS: seven music simulation cases and seven existing landing cases'
    $clockTests = [EndlessRollball.Tests.SoundPlaybackClockTests]::new()
    $clockTests.PlaybackStartsAfterLeadAndEndsAt45Seconds()
    $clockTests.PauseAndResumePreserveAudioAndGameplayTime()
    $clockTests.PauseBeforeScheduledStartKeepsAStartLeadOnResume()
    $clockTests.RestartClearsOldPauseAndPlaybackState()
    Write-Output 'PASS: four sound clock scheduling, pause/resume, and restart cases'
    $pianoTests = [EndlessRollball.Tests.PianoLandingSequenceTests]::new()
    $pianoTests.LandingReturnsTheAssignedMidiNotes()
    $pianoTests.SpawnEarlyAndDuplicateContactsDoNotPlayNotes()
    $pianoTests.MissedAndExpiredNotesAreSkippedAndRecoveryCanContinue()
    $pianoTests.RestartAllowsFirstNoteAgainAndEventEndIsSilent()
    $pianoTests.InvalidPitchAndMissingNoteGroupsAreRejected()
    Write-Output 'PASS: five MIDI note, silence, expiry, restart and validation cases'
    $rotationTests = [EndlessRollball.Tests.CloudSongRotationTests]::new()
    $rotationTests.EveryPieceIsPlayedBeforeRepeatingWithoutAdjacentRepeats()
    $rotationTests.SwitchingDifficultyKeepsEachLevelsOwnRotation()
    $rotationTests.SeedReproducesSelectionOrder()
    $rotationTests.UnknownDifficultyIsRejected()
    Write-Output 'PASS: four per-difficulty song rotation and repeat-prevention cases'
    $endingTests = [EndlessRollball.Tests.CloudMusicEndingTests]::new()
    $endingTests.PlanningPaddingIsOutsideTheVisibleCourse()
    $endingTests.BallSettlesOnFinalCloudWhileTheEndingRingsOut()
    $endingTests.MissingFinalCloudRecoversOntoItWithoutExposingPadding()
    Write-Output 'PASS: three final-platform, musical-tail and recovery cases'
    $chartFiles = @(Get-ChildItem Assets/Resources/CloudMusic -Filter '*.json' | Where-Object { $_.BaseName -match '^(Easy|Normal|Hard)(_[23])?$' })
    if ($chartFiles.Count -ne 9) { throw 'Expected exactly three piano pieces for each difficulty.' }
    foreach ($file in $chartFiles) {
        $name = $file.BaseName
        $data = Get-Content -Raw ("Assets/Resources/CloudMusic/"+$name+".json") | ConvertFrom-Json
        $chart = [EndlessRollball.CloudMusicChart]::new()
        foreach ($prop in $data.PSObject.Properties) {
            if ($prop.Name -eq 'chords') {
                $chart.chords = @($prop.Value | ForEach-Object {
                    $chord = [EndlessRollball.CloudPianoChord]::new()
                    $chord.notes = @($_.notes | ForEach-Object {
                        $note = [EndlessRollball.CloudPianoNote]::new()
                        $note.pitch = $_.pitch; $note.velocity = $_.velocity; $note.duration = $_.duration
                        $note
                    })
                    $chord
                })
            }
            else { $chart.($prop.Name) = $prop.Value }
        }
        $chart.ValidatePiano()
        $last = $chart.LastPlayableIndex
        if ($chart.times[$last] -ne 43 -or $chart.times[$last + 1] -le 45) { throw 'Invalid final platform / visible course boundary.' }
        foreach ($note in $chart.chords[$last].notes) {
            if ($chart.times[$last] + $note.duration + .5 -gt 45) { throw 'Final piano release would be cut off.' }
        }
        foreach ($frame in @((1.0/144),(1.0/60),(1.0/30))) {
            $run = [EndlessRollball.CloudMusicSimulation]::new($chart,1.14,1.04,.45,4)
            $frames = 0
            while (-not $run.Complete) {
                $frames++
                if ($frames -gt 10000) { throw 'Simulation failed to reach the completion boundary.' }
                $nextTime = [Math]::Min([double]45, [double]($run.Time + $frame))
                $steer = ($chart.lanes[$run.Next] * $chart.laneSpacing - $run.X) / ($chart.steeringSpeed * ($nextTime-$run.Time))
                $run.AdvanceTo($nextTime,$steer)
            }
            $expected = @($chart.times | Where-Object { $_ -gt 0 -and $_ -le 45 }).Count
            if ($run.Recoveries -ne 0 -or $run.Landings -ne $expected -or $run.Time -ne 45) {
                throw "$name frame=${frame}: landings=$($run.Landings) expected=$expected recoveries=$($run.Recoveries), time=$($run.Time)"
            }
            Write-Output "PASS: $name, frame interval $frame — $($run.Landings) landings, no recovery, exactly 45 seconds"
        }
    }
}
finally { Pop-Location }
