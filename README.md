Command-line program to record audio (what you hear) on windows

# Overview

A command-line program that can be used to record audio (what you hear) on windows, and the audio file format is .wav.  
The code for this is written in C# programming language with .NET 8.

# Download

* [The latest release](https://github.com/kenjihr/winsndrec/releases/latest).

# Usage

    winsndrec [OPTIONS]
    
    -o, --output              (Default: sound.wav) Output audio file name.
    
    -b, --bits-per-sample     Audio bits per sample. (16 or 24 or 32)
    
    -t, --truncate-silence    Threshold in decibel(dB) to truncate silence. The value can be from -10 to -100.
    
    --help                    Display this help screen.
    
    --version                 Display version information.

# Lisence

This project is licensed under the MIT License, see the LICENSE.txt file for details