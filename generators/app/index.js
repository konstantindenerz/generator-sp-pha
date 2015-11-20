var Generator = require('yeoman-generator'),
    chalk = require('chalk'),
    yosay = require('yosay'),
    path = require('path'),
    vsname = require('vs_projectname'),
    uuid = require('uuid');

var generator = Generator.Base.extend({
  constructor: function(){
    Generator.Base.apply(this, arguments);

    this.settings = {
      solutionName : vsname(this.arguments[0] || 'Undefined'),
      solutionNameClean: vsname(this.arguments[0] || 'Undefined').replace('.', '').replace('_',''),
      solutionAppSuffix: vsname('App'),
      solutionWebSuffix: vsname('Web'),
      solutionYear: '2015',
      clientId: '*',
      productId: uuid(),
      solutionId: uuid().toUpperCase(),
      projectAppId:  uuid().toUpperCase(),
      projectWebId:  uuid().toUpperCase(),
      certificatePath: '',
      certificatePassword: '',
      issuerId: '',
      globalAsaxStart: '%@',
      globalAsaxEnd: '%'
    };
  },
  info: function(){
    this.log(yosay(
      chalk.red('Welcome!') + '\n'+
      chalk.yellow('You\'re using the fantastic generator for scaffolding an ASP.NET web application with ')+
      chalk.blue('#SharePoint, #Angular ') +
      chalk.yellow(' and ') +
      chalk.blue('#Gulp') +
      chalk.yellow('. ')
    ));
  },
  copyApp: function(){
    var self = this;
    var directory = path.join('src',this.settings.solutionName + this.settings.solutionAppSuffix);
    this.sourceRoot(path.join(__dirname, 'templates/App'));
    function cpt(source, target){
      self.fs.copyTpl(self.templatePath(source),
      self.destinationPath(path.join(directory, target ? target : source )), self.settings);
    }
    function cp(source, target){
      self.copy(path.join(self.sourceRoot(),source),
      path.join(directory, target ? target : source ));
    }
    cpt('Solution.sln', this.settings.solutionName + '.sln');
    cpt('App.csproj', this.settings.solutionName +  this.settings.solutionAppSuffix + '.csproj');
    cpt('AppManifest.xml');
    cp('AppIcon.png')
  },
  copyWeb: function(){
    try{  }catch(exception){
        this.log(chalk.red(exception));
      }
    var self = this;
    var directory = path.join('src',this.settings.solutionName + this.settings.solutionWebSuffix);
    this.sourceRoot(path.join(__dirname, 'templates/Web'));
    function cpt(source, target){
      self.fs.copyTpl(self.templatePath(source),
      self.destinationPath(path.join(directory, target ? target : source )), self.settings);
    }
    function cp(source, target){
      self.copy(path.join(self.sourceRoot(),source),
      path.join(directory, target ? target : source ));
    }
    cpt('Web.csproj', this.settings.solutionName + this.settings.solutionWebSuffix + '.csproj');
    cpt('Web.config');
    cpt('Web.Debug.config');
    cpt('Web.Release.config');
    cpt('TokenHelper.cs');
    cpt('SharePointContext.cs');
    cpt('Global.asax.cs');
    cpt('Global.asax');
    cp('packages.config');
    cp('favicon.ico');
    //cp('App_Data');
    //cp('Models');
    cpt('Properties/AssemblyInfo.cs');
    cpt('Filters/**/**.*', 'Filters');
    cpt('Controllers/**/**.*', 'Controllers');
    cpt('App_Start/**/**.*', 'App_Start');
    cpt('Views/**/**.*', 'Views');
  },
  end: function(){

  }
})

module.exports = generator;
