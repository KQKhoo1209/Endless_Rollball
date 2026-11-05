"""Author original piano studies as standard MIDI plus compiled Unity landing charts.

No song recording or transcription is used. JSON and MIDI share the same note events.
Run from the Unity project root; uses only the Python standard library.
"""
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import uuid

ROOT = Path('Assets/Resources/CloudMusic')
MIDI = Path('Assets/EndlessRollballAssets/Sound/Cloud Piano MIDI')
PHRASES = [
    [72, 76, 79, 76, 74, 72, 71, 67],
    [69, 72, 76, 72, 71, 69, 67, 64],
    [65, 69, 72, 69, 67, 65, 64, 60],
    [67, 71, 74, 71, 72, 74, 76, 74],
]
BASS = [48, 45, 41, 43]
MELODIES = [PHRASES, [
    [76, 74, 72, 67, 69, 72, 74, 76],
    [76, 72, 69, 64, 67, 69, 71, 72],
    [77, 76, 72, 69, 72, 74, 72, 69],
    [74, 79, 77, 74, 71, 69, 71, 74],
], [
    [67, 72, 76, 74, 79, 76, 74, 72],
    [64, 69, 72, 71, 76, 72, 71, 69],
    [60, 65, 69, 67, 72, 69, 67, 65],
    [62, 67, 71, 69, 74, 71, 69, 67],
]]
TITLES = [
    ['Cloud Lullaby', 'Morning Mist', 'Floating Petals'],
    ['Cloud Wander', 'Silver Breeze', 'Sunlit Steps'],
    ['Cloud Spark', 'Starlight Run', 'Skyward Rush'],
]
TRANSPOSE = [0, 5, 2]
CONFIG = [
    ('Easy', 'Cloud Lullaby', 72, 6, 9, 2.5),
    ('Normal', 'Cloud Wander', 96, 8, 12, 1.75),
    ('Hard', 'Cloud Spark', 180, 12, 22, .75),
]


def meta(path, folder=False):
    p = Path(str(path) + '.meta')
    if not p.exists():
        p.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex +
                     ('\nfolderAsset: yes' if folder else '') + '\n', encoding='utf-8')


def vlq(value):
    result = [value & 127]
    value >>= 7
    while value:
        result.insert(0, (value & 127) | 128)
        value >>= 7
    return bytes(result)


def write_midi(path, chart):
    ppq = 480
    tempo = round(60_000_000 / chart['bpm'])
    events = [(0, b'\xff\x51\x03' + tempo.to_bytes(3, 'big')), (0, b'\xc0\x00')]
    for time, chord in zip(chart['times'], chart['chords']):
        if time >= 45:
            break
        for note in chord['notes']:
            on = round(time * 1_000_000 / tempo * ppq)
            off = round(min(45, time + note['duration']) * 1_000_000 / tempo * ppq)
            events.append((on, bytes([0x90, note['pitch'], note['velocity']])))
            events.append((off, bytes([0x80, note['pitch'], 0])))
    events.sort(key=lambda item: (item[0], item[1][0]))
    data, previous = bytearray(), 0
    for tick, message in events:
        data.extend(vlq(tick - previous)); data.extend(message); previous = tick
    end = round(45 * 1_000_000 / tempo * ppq)
    data.extend(vlq(end - previous)); data.extend(b'\xff\x2f\x00')
    path.write_bytes(b'MThd' + struct.pack('>IHHH', 6, 0, 1, ppq) + b'MTrk' + struct.pack('>I', len(data)) + data)
    meta(path)


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    MIDI.mkdir(parents=True, exist_ok=True)
    meta(MIDI, True)
    records = []
    for level, (mode, title, bpm, speed, steering, height) in enumerate(CONFIG):
        for variant in range(3):
            title = TITLES[level][variant]
            resource = mode if variant == 0 else mode + '_' + str(variant + 1)
            transpose = TRANSPOSE[variant]
            beat = 60 / bpm
            times = [0.]
            total_beats = 0
            while True:
                beats = 2 if mode == 'Easy' else 1
                if mode == 'Normal':
                    beats = 2 if (times[-1] < [10, 8, 12][variant] or [22, 20, 26][variant] <= times[-1] < [32, 30, 36][variant]) else 1
                total_beats += beats
                target = round(.1 + total_beats * beat, 6)
                if target >= 39:
                    break
                times.append(target)
            # Slow, resolving E-D-B-C cadence, then room for the final piano release.
            times.extend([40., 41., 42., 43.])
            final_platform = len(times) - 1
            # Hidden planning padding is never rendered or visited after the final cloud.
            times.extend([float(t) for t in range(46, 74, 3)])
            rng = random.Random(73100 + level + variant * 100)
            lanes, chords = [0], [dict(notes=[])]
            for i in range(1, len(times)):
                interval = times[i] - times[i-1]
                options = [lane for lane in [-1, 0, 1] if abs(lane-lanes[-1]) <= 1 and
                           abs(lane-lanes[-1])*4.5 <= steering*interval*.75]
                lanes.append(rng.choice(options))
                phrase = ((i-1)//8) % 4
                position = (i-1) % 8
                pitch = MELODIES[variant][phrase][position] + transpose
                duration = round(min(1.4, interval * .8), 6)
                velocity = (66 if mode == 'Easy' else 73 if mode == 'Normal' else 78) + [0, -6, 3, -4][position % 4]
                notes = [dict(pitch=pitch, velocity=velocity, duration=duration)]
                if position % (2 if mode == 'Easy' else 4) == 0:
                    notes += [dict(pitch=BASS[phrase] + transpose, velocity=45, duration=duration),
                              dict(pitch=BASS[phrase] + transpose + 7, velocity=38, duration=duration)]
                if 40 <= times[i] <= 43:
                    step = int(times[i] - 40)
                    notes = [dict(pitch=[76, 74, 71, 72][step] + transpose,
                                  velocity=[65, 61, 57, 55][step], duration=.7 if step < 3 else 1.45)]
                    if step == 3:
                        notes += [dict(pitch=p + transpose, velocity=37, duration=1.45) for p in [48, 52, 55]]
                chords.append(dict(notes=notes))
            # Last approach uses the centre lane, with ample steering time and a broad landing.
            lanes[final_platform] = 0
            chart = dict(title=title, composer='Original cloud piano study', difficulty=level,
                         duration=45., bpm=bpm, forwardSpeed=speed, steeringSpeed=steering,
                         laneSpacing=4.5, bounceHeight=height, endingTime=43., times=times, lanes=lanes, chords=chords)
            (ROOT/(resource+'.json')).write_text(json.dumps(chart, indent=2)+'\n', encoding='utf-8')
            meta(ROOT/(resource+'.json'))
            write_midi(MIDI/(resource+'.mid'), chart)
            record = dict(difficulty=mode, title=title, composer=chart['composer'],
                          description='Original diatonic piano melody with occasional root/fifth accompaniment, authored for this project.',
                          chartResource=resource,
                          midiAsset=str(MIDI/(resource+'.mid')).replace('\\', '/'),
                          midiSha256=hashlib.sha256((MIDI/(resource+'.mid')).read_bytes()).hexdigest(),
                          landingTargets=sum(0 < t < 45 for t in times))
            records.append(record)
            print(mode, title, '45-second targets:', record['landingTargets'], 'prepared platforms:', len(times))
    (ROOT/'Credits.json').write_text(json.dumps(dict(tracks=records, pianoSamples=dict(
        title='Salamander Grand Piano', author='Alexander Holm', license='CC BY 3.0',
        licenseUrl='https://creativecommons.org/licenses/by/3.0/',
        source='https://github.com/Tonejs/audio/tree/master/salamander',
        changes='Four original sample files used unchanged; repitched and enveloped at playback.')), indent=2)+'\n', encoding='utf-8')
    (ROOT/'Credits.txt').write_text(
        ' / '.join(record['title'] for record in records) + ': original piano studies authored for this project.\n' +
        'MIDI note sequences and JSON landing charts generated together by Tools/PrepareCloudPiano.py.\n\n'
        'Piano samples: Salamander Grand Piano by Alexander Holm, distributed by Tone.js.\n'
        'Licensed under CC BY 3.0: https://creativecommons.org/licenses/by/3.0/\n'
        'Source: https://github.com/Tonejs/audio/tree/master/salamander\n'
        'Sample files unchanged; repitched and enveloped at playback.\n', encoding='utf-8')


if __name__ == '__main__':
    main()
