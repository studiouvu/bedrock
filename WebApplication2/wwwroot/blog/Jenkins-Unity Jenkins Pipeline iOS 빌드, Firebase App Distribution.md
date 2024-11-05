
폴더 최상단

Jenkinsfile

```
UNITY_PATH = "/Applications/Unity/Hub/Editor/2022.3.50f1/Unity.app/Contents/MacOS/Unity"
pipeline {
	parameters {
        choice(name: 'TYPE', choices: ['Debug', 'Release'], description: '')
        string(name: 'BRANCH', defaultValue: 'develop')
		booleanParam(name: 'ANDROID', defaultValue: true, description: '')
		booleanParam(name: 'IOS', defaultValue: true, description: '')
        booleanParam(name: 'CLEAN', defaultValue: false, description: '')
        booleanParam(name: 'SKIP_PLAYMODE_TESTS', defaultValue: false, description: '')
    }
	agent any
	options {
        timestamps()
        // as a failsafe. our build tend around the 15min mark, so 45 would be excessive.
        timeout(time: 45, unit: 'MINUTES')
    }
	environment {
        BUILD_VERSION = readFile("${env.WORKSPACE}/Assets/BuildVersion/version.txt")
        BUILD_VERSION_NUMBER = readFile("${env.WORKSPACE}/Assets/BuildVersion/number.txt")
    }
	stages
	{
		stage('Start') {
            steps {
            	slackSend (channel: '#builds', message: "빌드 시작: Job '${env.JOB_NAME} #${env.BUILD_NUMBER}' (<${env.BUILD_URL}|Open>)\nBranch : ${env.BRANCH}")
            	sh "git reset --hard"
				sh "git checkout ${env.BRANCH}"
            	sh "git reset --hard"
            	sh "git pull"
            }
        }
        stage('Clean') {
            when {
                expression { return params.CLEAN }
            }
            steps {
            	sh "git clean --force -d -x"
                sh "if exist Library (rmdir Library /s /q)"
                sh "if exist Temp (rmdir Temp /s /q)"
            }
        }
        // stage ('Editmode Tests') {
        //     steps {
        //         sh "${UNITY_PATH} -nographics -batchmode -projectPath . -runTests -testResults editmodetests.xml -testPlatform editmode -logFile"
        //     }
        // }
        stage ('Build iOS') {
			when {
                expression { return params.IOS == true }
            }
            steps {
                sh "\"${UNITY_PATH}\" -nographics -buildTarget iOS -quit -batchmode -projectPath . -executeMethod Studiouvu.Editor.EditorBuild.PerformBuildIos -buildType \"${params.TYPE}\" -logFile"
            }
        }
		stage ('Archive') {
			when {
                expression { return params.IOS == true }
            }
            steps {
            	sh "xcodebuild -workspace ${env.WORKSPACE}/Builds/iOS/build/Unity-iPhone.xcworkspace -scheme Unity-iPhone -sdk iphoneos archive -archivePath ${env.WORKSPACE}/Builds/iOS/build/archive.xcarchive"
            }
        }
		stage ('Export IPA') {
			when {
                expression { return params.IOS == true }
            }
			steps {
            	sh "xcodebuild -exportArchive -archivePath ${env.WORKSPACE}/Builds/iOS/build/archive.xcarchive -exportOptionsPlist ${env.WORKSPACE}/exportOptionsAdHoc.plist -exportPath ${env.WORKSPACE}/Builds/iOS/ipa -allowProvisioningUpdates"
            }
        }
		stage ('Distribution Firebase iOS') {
			when {
                expression { return params.IOS == true }
            }
            steps {
            	sh "firebase appdistribution:distribute ${env.WORKSPACE}/Builds/iOS/ipa/toast.ipa --app (app-id) --release-notes '${env.JOB_NAME} #${env.BUILD_NUMBER}' --groups 'team'"
            }
        }
		stage ('Commit') {
            steps {
				sh "git tag '${BUILD_VERSION}/${BUILD_VERSION}.${BUILD_VERSION_NUMBER}'"
				sh "git push origin --tags"

				sh "git checkout ${env.BRANCH}"
            	sh "git pull"
				sh "git commit -am 'Updated version number'"
				sh "git push origin ${env.BRANCH}"
            }
        }
	}
	post {
		success {
			slackSend (channel: '#builds', color: '#2eb885', message: "빌드 성공: Job '${env.JOB_NAME} #${env.BUILD_NUMBER}' (<${env.BUILD_URL}|Open>)\nBranch : ${env.BRANCH}")
		}
		failure {
			slackSend (channel: '#builds', color: '#a30300', message: "빌드 실패: Job '${env.JOB_NAME} #${env.BUILD_NUMBER}' (<${env.BUILD_URL}|Open>)\nBranch : ${env.BRANCH}")
		}
        aborted {
			slackSend (channel: '#builds', color: '#d9a138', message: "빌드 취소: Job '${env.JOB_NAME} #${env.BUILD_NUMBER}' (<${env.BUILD_URL}|Open>)\nBranch : ${env.BRANCH}")
		}
    }
```


exportOptionsAdHoc.plist

```
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key>
	<string>ad-hoc</string>
</dict>
</plist>
```


Unity 스크립트

```
public class EditorBuild : MonoBehaviour
    {
        private static string VersionPath => Application.dataPath + "/BuildVersion/version.txt";
        private static string BuildNumberPath => Application.dataPath + "/BuildVersion/number.txt";

        private static string[] scenes = FindEnabledEditorScenes();

        public static void PerformBuildIos()
        {
            Debug.Log($"[EditorBuild] PerformBuildIos");

            SetBuildVersion();

            BuildPipeline.BuildPlayer(FindEnabledEditorScenes(),
                "Builds/iOS/build", BuildTarget.iOS, BuildOptions.None);
            
            IncreaseBuildNumber();
            SetBuildVersion();
        }
        
        [MenuItem("Tools/Build/SetBuildVersion")]
        private static void SetBuildVersion()
        {
            Debug.Log($"[EditorBuild] SetBuildVersion");
            var version = File.ReadAllText(VersionPath);
            var buildNumber = File.ReadAllText(BuildNumberPath);
            PlayerSettings.bundleVersion = $"{version}.{buildNumber}";
            PlayerSettings.Android.bundleVersionCode = int.Parse(buildNumber);
            PlayerSettings.iOS.buildNumber = buildNumber;
        }

        [MenuItem("Tools/Build/IncreaseBuildNumber")]
        private static void IncreaseBuildNumber()
        {
            Debug.Log($"[EditorBuild] IncreaseBuildNumber");
            var buildNumberString = File.ReadAllText(BuildNumberPath);
            var buildNumber = int.Parse(buildNumberString);
            buildNumber += 1;
            File.WriteAllText(BuildNumberPath, buildNumber.ToString());
        }

        private static string[] FindEnabledEditorScenes()
        {
            var editorScenes = new List<string>();

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled) continue;
                editorScenes.Add(scene.path);
            }

            return editorScenes.ToArray();
        }
    }
```


참고 문서

- [https://www.jenkins.io/doc/book/pipeline/syntax/](https://www.jenkins.io/doc/book/pipeline/syntax/)
- [https://andrewfray.wordpress.com/2020/12/28/building-unity-using-jenkins-pipelines/](https://andrewfray.wordpress.com/2020/12/28/building-unity-using-jenkins-pipelines/)
- [https://ios-development.tistory.com/1102](https://ios-development.tistory.com/1102)
- [https://gist.github.com/cocoaNib/502900f24846eb17bb29](https://gist.github.com/cocoaNib/502900f24846eb17bb29)
- [https://stackoverflow.com/questions/36934028/get-absolute-path-to-workspace-directory-in-jenkins-pipeline-plugin](https://stackoverflow.com/questions/36934028/get-absolute-path-to-workspace-directory-in-jenkins-pipeline-plugin)
- [https://medium.com/trendyol-tech/building-an-ios-distribution-pipeline-creating-a-freestyle-jenkins-project-part-2-688497f1a712](https://medium.com/trendyol-tech/building-an-ios-distribution-pipeline-creating-a-freestyle-jenkins-project-part-2-688497f1a712)
- [https://1minute-before6pm.tistory.com/52](https://1minute-before6pm.tistory.com/52)
- [https://jong-bae.tistory.com/27](https://jong-bae.tistory.com/27)
- [https://stackoverflow.com/questions/1146973/how-do-i-revert-all-local-changes-in-git-managed-project-to-previous-state](https://stackoverflow.com/questions/1146973/how-do-i-revert-all-local-changes-in-git-managed-project-to-previous-state)
- [https://stackoverflow.com/questions/22917491/reading-file-from-workspace-in-jenkins-with-groovy-script](https://stackoverflow.com/questions/22917491/reading-file-from-workspace-in-jenkins-with-groovy-script)
- [https://minsone.github.io/git/git-addtion-and-modified-delete-tag](https://minsone.github.io/git/git-addtion-and-modified-delete-tag)
- [https://www.jenkins.io/doc/book/pipeline/jenkinsfile/#using-environment-variables](https://www.jenkins.io/doc/book/pipeline/jenkinsfile/#using-environment-variables)
- [https://www.reddit.com/r/jenkinsci/comments/fccjy4/jenkins_plugin_reading_a_file_in_a_pipeline_step/](https://www.reddit.com/r/jenkinsci/comments/fccjy4/jenkins_plugin_reading_a_file_in_a_pipeline_step/)
- [https://www.jenkins.io/doc/pipeline/steps/workflow-basic-steps/#readfile-read-file-from-workspace](https://www.jenkins.io/doc/pipeline/steps/workflow-basic-steps/#readfile-read-file-from-workspace)
- [https://docs.unity3d.com/2019.1/Documentation/Manual/CommandLineArguments.html](https://docs.unity3d.com/2019.1/Documentation/Manual/CommandLineArguments.html)