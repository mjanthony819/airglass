; AirGlass per-user installer (no admin rights needed).
; Build: makensis -DPUBLISH_DIR=..\publish installer\AirGlass.nsi
!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif

Name "AirGlass"
OutFile "AirGlass-Setup-${VERSION}.exe"
Unicode true
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\AirGlass"
SetCompressor /SOLID lzma

Icon "${__FILEDIR__}\..\assets\airglass.ico"
UninstallIcon "${__FILEDIR__}\..\assets\airglass.ico"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  SetOutPath $INSTDIR
  File "${PUBLISH_DIR}\AirGlass.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\AirGlass"
  CreateShortcut "$SMPROGRAMS\AirGlass\AirGlass.lnk" "$INSTDIR\AirGlass.exe"
  CreateShortcut "$DESKTOP\AirGlass.lnk" "$INSTDIR\AirGlass.exe"

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "DisplayName" "AirGlass"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "DisplayIcon" "$INSTDIR\AirGlass.exe,0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "Publisher" "M J Anthony"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass" \
    "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\AirGlass.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\AirGlass\AirGlass.lnk"
  RMDir "$SMPROGRAMS\AirGlass"
  Delete "$DESKTOP\AirGlass.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AirGlass"
SectionEnd
